"""Fail-closed classification of the exact proposed upstream merge."""
import argparse
import json
from pathlib import PurePosixPath, Path
import re
import subprocess


def git(*args):
    return subprocess.check_output(['git', *args], text=True).strip()


def paths(before, after):
    return set(filter(None, subprocess.check_output(
        ['git', 'diff', '--name-only', '--no-renames', '-z', before, after]
    ).decode('utf-8').split('\0')))


ROOT = Path(__file__).resolve().parents[1]
PROJECT = 'Stylist/Stylist.csproj'
SENSITIVE_SOURCE = {
    'Stylist/Utils.cs', 'Stylist/Stylist.cs', 'Stylist/InventoryDescriptor.cs',
    'Stylist/Services/ItemMover.cs', 'Stylist/Services/GearsetStorage.cs',
    'Stylist/Services/IpcProvider.cs', 'Stylist/Services/Memory.cs',
    'Stylist/Services/ServiceManager.cs', 'Stylist/Global.cs',
}

FORBIDDEN = re.compile(r'GearsetStorage|UnmoveItems|ItemsToMove|ItemsToUnmove|OnKeybindPressed|AnchorRight|AnchorBottom|HasCustomPosition|FavouritePlugins|HotbarAnchor|HotbarSlotNode|HotbarOverlayNode|HotbarConfig|System\.Config|RaptureHotbarModule|FFXIVClientStructs|Marshal|NativeLibrary|UnmanagedCallersOnly|PackageReference|ProjectReference|DllImport|LibraryImport|Reflection|GetMethod|GetField|GetProperty|Activator|\bunsafe\b|\bdynamic\b')


def content(ref, path):
    return subprocess.check_output(['git', 'show', f'{ref}:{path}']).decode('utf-8-sig')


def version_only(before, after):
    """Only one plain numeric Version value may differ; all other bytes match."""
    import xml.etree.ElementTree as xml
    if any(marker in text for text in (before, after) for marker in ('<!DOCTYPE', '<!ENTITY')):
        return False
    try:
        for text in (before, after):
            root = xml.fromstring(text)
            versions = root.findall('./PropertyGroup/Version')
            if len(versions) != 1 or versions[0].attrib:
                return False
        pattern = r'(<Version>)([0-9]+(?:\.[0-9]+){2,3})(</Version>)'
        old, new = re.findall(pattern, before), re.findall(pattern, after)
        if len(old) != 1 or len(new) != 1:
            return False
        number = lambda value: tuple(map(int, value.split('.'))) + (0,) * (4 - len(value.split('.')))
        return (number(new[0][1]) > number(old[0][1])
                and re.sub(pattern, r'\1VERSION\3', before) == re.sub(pattern, r'\1VERSION\3', after))
    except xml.ParseError:
        return False


def source_snapshot(ref):
    """Read exact tracked blobs in one batch, independent of export attributes."""
    import io
    names = subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', '-z', ref, '--', 'Stylist']).decode().split('\0')
    names = [name for name in names if name.endswith('.cs')]
    if any('\n' in name or '\r' in name for name in names):
        raise ValueError('Unsupported source filename')
    request = ''.join(f'{ref}:{name}\n' for name in names).encode()
    blobs = io.BytesIO(subprocess.check_output(['git', 'cat-file', '--batch'], input=request))
    result = {}
    for name in names:
        _, kind, size = blobs.readline().split()
        if kind != b'blob':
            raise ValueError('Source is not a regular blob')
        result[name] = blobs.read(int(size)).decode('utf-8-sig')
        if blobs.read(1) != b'\n':
            raise ValueError('Invalid blob stream')
    return result


_REVIEW_DLL = None


def syntax_review(common, base, upstream, head, candidates, routine_conditions):
    import os
    global _REVIEW_DLL
    if _REVIEW_DLL is None:
        trusted = os.environ.get('UPSTREAM_REVIEW_DLL')
        if trusted:
            _REVIEW_DLL = Path(trusted)
        else:
            output = ROOT / 'obj' / 'upstream-review'
            subprocess.run(['dotnet', 'build', str(ROOT / 'tools/UpstreamReview/UpstreamReview.csproj'),
                            '-c', 'Release', '-o', str(output), '--nologo'], check=True,
                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=120)
            _REVIEW_DLL = output / 'UpstreamReview.dll'
    payload = dict(Shared=source_snapshot(common), Fork=source_snapshot(base),
                   Incoming=source_snapshot(upstream), Merged=source_snapshot(head), Paths=candidates,
                   RoutineConditions=routine_conditions)
    raw = subprocess.check_output(['dotnet', str(_REVIEW_DLL)], input=json.dumps(payload).encode(), timeout=90)
    return json.loads(raw)


def classify(base, upstream, head):
    base, upstream, head = [git('rev-parse', f'{ref}^{{commit}}') for ref in (base, upstream, head)]
    common = git('merge-base', base, upstream)
    fork_paths = paths(common, base)
    changed = paths(base, head) | paths(common, upstream)
    reasons, accepted, candidates, routine_conditions = [], [], [], []
    expected = subprocess.run(['git', 'merge-tree', '--write-tree', base, upstream], text=True, capture_output=True)
    trees = expected.stdout.splitlines()
    if expected.returncode or not trees or trees[0] != git('rev-parse', f'{head}^{{tree}}'):
        return {'base': base, 'upstream': upstream, 'head': head, 'routine': False,
                'changed_paths': sorted(changed), 'accepted': [],
                'reasons': ['Proposed tree differs from a clean merge of the recorded upstream and fork tips.']}
    for path in sorted(changed):
        p = PurePosixPath(path)
        modes = [git('ls-tree', ref, '--', path).split(' ', 1)[0] for ref in (common, upstream, base, head)]
        if any(mode and mode != '100644' for mode in modes):
            reasons.append(f'{path}: special file mode, executable, symlink or submodule.')
            continue
        if path == PROJECT:
            try:
                if (version_only(content(common, path), content(upstream, path))
                        and version_only(content(base, path), content(head, path))):
                    accepted.append(f'{path}: numeric upstream version increase only; fork release version/identity remain separate.')
                    continue
            except (subprocess.CalledProcessError, UnicodeError):
                pass
            reasons.append(f'{path}: project/SDK/dependency/build metadata changed beyond the numeric Version value.')
            continue
        routine_doc = (path == 'README.md' or path.startswith('docs/')) and p.suffix == '.md'
        if routine_doc:
            if path in fork_paths and not modes[3]:
                reasons.append(f'{path}: deleting fork documentation needs review.')
            else:
                accepted.append(f'{path}: cleanly merged documentation.')
            continue
        diff = git('diff', '--no-ext-diff', '--no-textconv', '--unified=0', base, head, '--', path)
        edits = '\n'.join(line[1:] for line in diff.splitlines() if line[:1] in ('+', '-') and not line.startswith(('+++', '---')))
        if FORBIDDEN.search(edits):
            reasons.append(f'{path}: edit references fork hooks, reflection or native interop.')
            continue
        routine_condition = p.parent.as_posix() == 'Stylist/Conditions' and p.suffix == '.cs' and p.name != 'ConditionBase.cs'
        if routine_condition and path not in fork_paths and all(modes):
            candidates.append(path)
            routine_conditions.append(path)
            continue
        if path in SENSITIVE_SOURCE or path.startswith('Stylist/Configuration/'):
            reasons.append(f'{path}: saved-data, activation/input or plugin-lifecycle boundary requires review.')
            continue
        if (path.startswith('Stylist/') and p.suffix == '.cs'
                and all(modes)):
            candidates.append(path)
            continue
        reasons.append(f'{path}: dependency, publishing, lifecycle, fork-only or unrecognised file requires review.')
    if candidates:
        try:
            review = syntax_review(common, base, upstream, head, candidates, routine_conditions)
            reasons.extend(review['reasons'])
            if not review['reasons']:
                accepted.extend(f'{path}: existing method bodies outside protected members/contracts and transitive dependencies.' for path in candidates)
        except (subprocess.SubprocessError, OSError, ValueError, KeyError) as error:
            reasons.append(f'Syntax review unavailable or failed; manual review required ({type(error).__name__}).')
    if not changed:
        reasons.append('No incoming changes to merge.')
    return {'base': base, 'upstream': upstream, 'head': head, 'routine': not reasons,
            'changed_paths': sorted(changed), 'accepted': accepted, 'reasons': list(dict.fromkeys(reasons))}

def report(policy):
    decision = 'Routine: eligible for automatic merge after all checks pass.' if policy['routine'] else 'Manual review required; automatic merge is disabled.'
    lines = [decision, '', 'Checks: build, configuration regressions, policy regressions and release-package validation.',
             'Routine automatic merges queue a validated, versioned release. Sensitive updates remain manual.', '', f"Validated base: `{policy['base']}`",
             f"Proposed head: `{policy['head']}`", '', 'Changed files:']
    lines.extend(f'- `{path}`' for path in policy['changed_paths'])
    if policy.get('accepted'):
        lines.extend(['', 'Automatic review evidence:'])
        lines.extend(f'- {entry}' for entry in policy['accepted'])
    if policy['reasons']:
        lines.extend(['', 'Reasons for manual review:'])
        lines.extend(f'- {reason}' for reason in policy['reasons'])
    if policy['routine']:
        receipt = {key: policy[key] for key in ['base', 'upstream', 'head']}
        lines.extend(['', '<!-- routine-release:' + json.dumps(receipt) + ' -->'])
    return '\n'.join(lines) + '\n'


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', required=True)
    parser.add_argument('--upstream', required=True)
    parser.add_argument('--head', default='HEAD')
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    policy = classify(args.base, args.upstream, args.head)
    Path(args.output).write_text(json.dumps(policy, indent=2) + '\n', encoding='utf-8')
    Path(args.output + '.md').write_text(report(policy), encoding='utf-8')
    print(report(policy))
