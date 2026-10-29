"""Exercise classification and atomic merging against real isolated Git histories."""
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]


def module(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'tools' / (name + '.py'))
    loaded = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loaded)
    return loaded


policy_tool = module('upstream_policy')
merge_tool = module('merge_routine_upstream')


class UpstreamPolicyTests(unittest.TestCase):
    def setUp(self):
        parent = ROOT / 'obj' / 'policy-tests'
        parent.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=parent)
        self.folder = Path(self.temp.name).resolve()
        assert self.folder.is_relative_to(parent.resolve())
        self.addCleanup(self.temp.cleanup)
        self.old_cwd = Path.cwd()
        os.chdir(self.folder)
        self.addCleanup(os.chdir, self.old_cwd)
        self.git('init', '-b', 'master')
        self.git('config', 'user.name', 'Policy tests')
        self.git('config', 'user.email', 'tests@example.invalid')
        self.example = 'namespace Samples;\npublic class Example {\n    public int First() => 1;\n\n    public int Second() => 2;\n}\n'
        self.write('Stylist/Conditions/Example.cs', self.example)
        self.source = ('namespace Samples;\npublic class Helper {\n'
                       '    public int UsedByFork() => 1;\n\n'
                       '    public int Unrelated() => 2;\n}\n')
        self.write('Stylist/Classes/Helper.cs', self.source)
        self.project = '<Project Sdk="Dalamud.NET.Sdk/15.0.0">\n<PropertyGroup><Version>1.0.1.0</Version></PropertyGroup>\n</Project>\n'
        self.write('Stylist/Stylist.csproj', self.project)
        self.write('README.md', 'upstream readme\n')
        self.shared_doc = 'upstream intro\n\n\n\n\n\nupstream installation\n'
        self.write('docs/shared.md', self.shared_doc)
        self.git('add', '.')
        self.git('commit', '-m', 'Shared history')
        self.shared = self.git('rev-parse', 'HEAD')
        self.git('branch', 'upstream')
        self.write('Stylist/Configuration/HotbarConfig.cs', 'namespace Samples;\npublic class HotbarConfig { public bool UnmoveItems; }\n')
        self.write('Stylist/Classes/ForkFeature.cs', 'namespace Samples;\npublic class ForkFeature { public int Run() => new Helper().UsedByFork(); }\n')
        self.write('README.md', 'fork installation instructions\n')
        self.write('docs/shared.md', self.shared_doc.replace('upstream installation', 'fork installation'))
        self.git('add', '.')
        self.git('commit', '-m', 'Fork features')
        self.base = self.git('rev-parse', 'HEAD')

    def git(self, *args):
        result = subprocess.run(['git', *args], text=True, capture_output=True, check=True)
        return result.stdout.strip()

    def write(self, path, content):
        file = self.folder / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(content, encoding='utf-8')

    def propose(self, path, content):
        self.git('switch', '-C', 'upstream', self.shared)
        self.write(path, content)
        self.git('add', '.')
        self.git('commit', '-m', 'Upstream change')
        self.upstream = self.git('rev-parse', 'HEAD')
        self.git('switch', '--detach', self.base)
        self.git('merge', '--no-edit', self.upstream)
        return policy_tool.classify(self.base, self.upstream, 'HEAD')

    def test_routine_condition_and_documentation(self):
        for path in ['Stylist/Conditions/Example.cs', 'docs/usage.md']:
            with self.subTest(path=path):
                result = self.propose(path, self.example.replace('Second() => 2', 'Second() => 20') if path.endswith('.cs') else 'routine adjustment\n')
                self.assertTrue(result['routine'], result['reasons'])

    def test_condition_schema_and_native_edits_cannot_use_routine_exception(self):
        for text in [self.example.replace('public class Example {', 'public class Example { public int SavedField;'),
                     'using FFXIVClientStructs.FFXIV.Client.Game;\n' + self.example,
                     self.example.replace('Second()', 'Second(int argument)')]:
            result = self.propose('Stylist/Conditions/Example.cs', text)
            self.assertFalse(result['routine'], text)

    def test_standard_condition_evaluation_remains_routine(self):
        self.git('switch', 'upstream')
        source = ('namespace Samples;\npublic abstract class Evaluation { protected abstract bool EvaluateCondition(); public bool Run() => EvaluateCondition(); }\n'
                  'public static class Service { public static bool Get() => true; }\n'
                  'public static class Other { public static bool Get() => false; }\n'
                  'public class Example : Evaluation { private bool Allowed = true; protected override bool EvaluateCondition() => Allowed && Service.Get(); }\n')
        self.write('Stylist/Conditions/Example.cs', source)
        self.git('add', '.')
        self.git('commit', '-m', 'Shared condition evaluation')
        self.shared = self.git('rev-parse', 'HEAD')
        self.git('switch', '--detach', self.base)
        self.git('merge', '--no-edit', self.shared)
        self.write('Stylist/Classes/ConditionUser.cs', 'namespace Samples; public class ConditionUser { public bool Get() => new Example().Run(); }\n')
        self.git('add', '.')
        self.git('commit', '-m', 'Fork consumes existing condition')
        self.base = self.git('rev-parse', 'HEAD')
        for text, expected in [(source.replace('EvaluateCondition() => Allowed', 'EvaluateCondition() => !Allowed'), True),
                               (source.replace('Allowed && Service.Get()', 'Allowed && Other.Get()'), False),
                               (source.replace('Allowed && Service.Get()', 'Allowed && Service.Get() && Service.Get()'), False)]:
            result = self.propose('Stylist/Conditions/Example.cs', text)
            self.assertEqual(result['routine'], expected, result['reasons'])

    def test_dependencies_publishing_configuration_and_unknown_files_need_review(self):
        for path in ['.github/workflows/release.yml', '.gitmodules', 'global.json',
                     'NuGet.config', 'Directory.Build.props', 'Stylist/Stylist.csproj',
                     'Stylist/packages.lock.json', 'KamiToolKit/source.cs',
                     'Stylist/Nodes/NewNode.cs', 'Stylist/Configuration/NewConfig.cs',
                     'Stylist/Conditions/ConditionBase.cs', 'tools/new_check.py',
                     'pluginmaster.json', 'assets/icons/icon.png', 'LICENSE.md', 'new-unknown-file.cs']:
            with self.subTest(path=path):
                result = self.propose(path, 'upstream update\n')
                self.assertFalse(result['routine'], path)

    def test_fork_modified_files_need_review_even_when_allowlisted(self):
        self.git('switch', 'master')
        self.write('Stylist/Conditions/Example.cs', self.example.replace('\n}', '\n    public int ForkHook() => First();\n}'))
        self.git('add', '.')
        self.git('commit', '-m', 'Fork now customises a condition')
        self.base = self.git('rev-parse', 'HEAD')
        result = self.propose('Stylist/Conditions/Example.cs', self.example.replace('First() => 1', 'First() => 10'))
        self.assertFalse(result['routine'])
        self.assertTrue(any('protected' in reason for reason in result['reasons']), result['reasons'])

    def test_version_only_increase_is_routine(self):
        result = self.propose('Stylist/Stylist.csproj', self.project.replace('1.0.1.0', '1.0.2.0'))
        self.assertTrue(result['routine'], result['reasons'])
        self.assertTrue(any('numeric upstream version' in evidence for evidence in result['accepted']))

    def test_clean_documentation_update_preserves_fork_section(self):
        result = self.propose('docs/shared.md', self.shared_doc.replace('upstream intro', 'improved upstream intro'))
        self.assertTrue(result['routine'], result['reasons'])
        self.assertIn('fork installation', Path('docs/shared.md').read_text())

    def test_version_bump_cannot_hide_dependency_or_build_changes(self):
        for text in [self.project.replace('15.0.0', '16.0.0'),
                     self.project.replace('</Project>', '<ItemGroup><PackageReference Include="Other" Version="1.0.0" /></ItemGroup></Project>'),
                     self.project.replace('</Project>', '<Target Name="Execute" BeforeTargets="Build" /></Project>'),
                     self.project.replace('<Version>', '<Version Condition="true">'),
                     self.project.replace('</PropertyGroup>', '<Version>9.0.0</Version></PropertyGroup>'),
                     self.project.replace('1.0.1.0', '0.9.0.0')]:
            result = self.propose('Stylist/Stylist.csproj', text.replace('1.0.1.0', '1.0.2.0'))
            self.assertFalse(result['routine'], text)

    def test_unrelated_existing_method_is_routine_even_in_shared_file(self):
        result = self.propose('Stylist/Classes/Helper.cs', self.source.replace('Unrelated() => 2', 'Unrelated() => 20'))
        self.assertTrue(result['routine'], result['reasons'])

    def test_unrelated_member_in_fork_modified_file_is_routine(self):
        self.write('Stylist/Classes/Helper.cs', self.source.replace('UsedByFork() => 1', 'UsedByFork() => 10'))
        self.git('add', '.')
        self.git('commit', '-m', 'Fork modifies one member in shared file')
        self.base = self.git('rev-parse', 'HEAD')
        result = self.propose('Stylist/Classes/Helper.cs', self.source.replace('Unrelated() => 2', 'Unrelated() => 20'))
        self.assertTrue(result['routine'], result['reasons'])

    def test_transitive_feature_dependency_needs_review(self):
        result = self.propose('Stylist/Classes/Helper.cs', self.source.replace('UsedByFork() => 1', 'UsedByFork() => 10'))
        self.assertFalse(result['routine'])
        self.assertTrue(any('protected' in reason for reason in result['reasons']), result['reasons'])

    def test_multihop_dependency_in_another_file_needs_review(self):
        self.git('switch', 'upstream')
        self.write('Stylist/Classes/Helper.cs', self.source.replace('UsedByFork() => 1', 'UsedByFork() => new Leaf().Value()'))
        leaf = 'namespace Samples;\npublic class Leaf { public int Value() => 1; }\n'
        self.write('Stylist/Classes/Leaf.cs', leaf)
        self.git('add', '.')
        self.git('commit', '-m', 'Shared helper delegates to leaf')
        self.shared = self.git('rev-parse', 'HEAD')
        self.git('switch', '--detach', self.base)
        self.git('merge', '--no-edit', self.shared)
        self.base = self.git('rev-parse', 'HEAD')
        result = self.propose('Stylist/Classes/Leaf.cs', leaf.replace('Value() => 1', 'Value() => 2'))
        self.assertFalse(result['routine'])
        self.assertTrue(any('protected' in reason for reason in result['reasons']), result['reasons'])

    def test_comments_do_not_impersonate_a_protected_member(self):
        result = self.propose('Stylist/Classes/Helper.cs', self.source.replace('Unrelated() => 2', 'Unrelated() => 20 /* public int UsedByFork() => 999; */'))
        self.assertTrue(result['routine'], result['reasons'])

    def test_changes_to_disabled_source_need_review(self):
        self.git('switch', 'upstream')
        source = self.source + '\n#if UNUSED\nclass Disabled { int Value = 1; }\n#endif\n'
        self.write('Stylist/Classes/Helper.cs', source)
        self.git('add', '.')
        self.git('commit', '-m', 'Shared disabled source')
        self.shared = self.git('rev-parse', 'HEAD')
        self.git('switch', '--detach', self.base)
        self.git('merge', '--no-edit', self.shared)
        self.base = self.git('rev-parse', 'HEAD')
        result = self.propose('Stylist/Classes/Helper.cs', source.replace('Value = 1', 'Value = 2'))
        self.assertFalse(result['routine'])

    def test_calling_protected_code_from_previously_unrelated_method_needs_review(self):
        result = self.propose('Stylist/Classes/Helper.cs', self.source.replace('Unrelated() => 2', 'Unrelated() => UsedByFork()'))
        self.assertFalse(result['routine'])

    def test_signatures_fields_types_imports_directives_and_unsafe_need_review(self):
        for text in [self.source.replace('Unrelated()', 'Unrelated(int argument)'),
                     self.source.replace('public class Helper', 'public class Helper : System.IDisposable'),
                     self.source.replace('public class Helper {', 'public class Helper { public int Saved = 1;'),
                     self.source.replace('namespace Samples;', 'using System.IO;\nnamespace Samples;'),
                     '#define CHANGED\n' + self.source,
                     self.source.replace('public int Unrelated', 'public unsafe int Unrelated'),
                     self.source.replace('Unrelated() => 2', 'Unrelated() => typeof(Helper).GetMethod("UsedByFork") is null ? 1 : 0'),
                     self.source.replace('Unrelated() => 2', 'Unrelated() => { invalid syntax')]:
            result = self.propose('Stylist/Classes/Helper.cs', text)
            self.assertFalse(result['routine'], text)

    def test_missing_reviewer_fails_closed(self):
        with patch.object(policy_tool, 'syntax_review', side_effect=OSError('parser unavailable')):
            result = self.propose('Stylist/Classes/Helper.cs', self.source.replace('Unrelated() => 2', 'Unrelated() => 20'))
        self.assertFalse(result['routine'])
        self.assertTrue(any('unavailable' in reason for reason in result['reasons']))

    def test_saved_data_and_input_helpers_remain_manual(self):
        for path in ['Stylist/Classes/HotbarLocationConverter.cs', 'Stylist/Classes/PresetManager.cs',
                     'Stylist/Configuration/OtherConfig.cs', 'Stylist/Extensions/KeybindExtensions.cs',
                     'Stylist/Extensions/KeyStateExtensions.cs']:
            result = self.propose(path, 'namespace Samples; public class Contract { public int Unrelated() => 20; }\n')
            self.assertFalse(result['routine'], path)

    def test_existing_native_structure_import_remains_manual(self):
        self.git('switch', 'upstream')
        source = 'using FFXIVClientStructs.FFXIV.Client.Game;\n' + self.source
        self.write('Stylist/Classes/Helper.cs', source)
        self.git('add', '.')
        self.git('commit', '-m', 'Shared native import')
        self.shared = self.git('rev-parse', 'HEAD')
        self.git('switch', '--detach', self.base)
        self.git('merge', '--no-edit', self.shared)
        self.base = self.git('rev-parse', 'HEAD')
        result = self.propose('Stylist/Classes/Helper.cs', source.replace('Unrelated() => 2', 'Unrelated() => 20'))
        self.assertFalse(result['routine'])
        self.assertTrue(any('native client' in reason for reason in result['reasons']), result['reasons'])

    def test_references_to_fork_hooks_or_native_interop_need_review(self):
        for content in ['UnmoveItems', 'OnKeybindPressed', 'unsafe void Execute()', '[DllImport("x")]']:
            result = self.propose('Stylist/Conditions/Example.cs', content + '\n')
            self.assertFalse(result['routine'], content)

    def test_additional_update_branch_edits_are_not_treated_as_upstream(self):
        result = self.propose('docs/usage.md', 'routine docs\n')
        self.assertTrue(result['routine'])
        self.write('docs/extra.md', 'unexpected branch edit\n')
        self.git('add', '.')
        self.git('commit', '-m', 'Extra update-branch edit')
        result = policy_tool.classify(self.base, self.upstream, 'HEAD')
        self.assertFalse(result['routine'])
        self.assertTrue(any('differs from a clean merge' in reason for reason in result['reasons']))

    def test_removed_condition_needs_review(self):
        self.git('switch', 'upstream')
        self.git('rm', 'Stylist/Conditions/Example.cs')
        self.git('commit', '-m', 'Remove saved condition type')
        upstream = self.git('rev-parse', 'HEAD')
        self.git('switch', '--detach', self.base)
        self.git('merge', '--no-edit', upstream)
        self.assertFalse(policy_tool.classify(self.base, upstream, 'HEAD')['routine'])

    def test_merge_conflict_needs_manual_review(self):
        self.git('switch', 'upstream')
        self.write('README.md', 'conflicting upstream installation\n')
        self.git('add', '.')
        self.git('commit', '-m', 'Conflict with fork readme')
        upstream = self.git('rev-parse', 'HEAD')
        result = policy_tool.classify(self.base, upstream, upstream)
        self.assertFalse(result['routine'])
        self.assertTrue(any('differs from a clean merge' in reason for reason in result['reasons']))

    def test_submodule_with_routine_looking_name_needs_review(self):
        self.git('switch', 'upstream')
        self.git('update-index', '--add', '--cacheinfo', f'160000,{self.shared},docs/library.md')
        self.git('commit', '-m', 'Add dependency disguised as documentation')
        upstream = self.git('rev-parse', 'HEAD')
        self.git('switch', '--detach', self.base)
        self.git('merge', '--no-edit', upstream)
        self.assertFalse(policy_tool.classify(self.base, upstream, 'HEAD')['routine'])

    def prepare_remote(self, policy):
        remote_temp = tempfile.TemporaryDirectory(dir=self.folder.parent)
        remote = Path(remote_temp.name).resolve()
        assert remote.is_relative_to(self.folder.parent)
        self.addCleanup(remote_temp.cleanup)
        self.git('init', '--bare', str(remote))
        self.git('remote', 'add', 'origin', str(remote))
        self.git('push', 'origin', f'{policy["base"]}:refs/heads/master',
                 f'{policy["head"]}:refs/heads/codex/upstream-sync')
        info = {'state': 'OPEN', 'isDraft': False, 'isCrossRepository': False,
                'headRefOid': policy['head'], 'baseRefOid': policy['base'],
                'headRefName': 'codex/upstream-sync', 'baseRefName': 'master',
                'author': {'login': 'app/github-actions', 'is_bot': True}}
        real_output = merge_tool.output

        def with_fake_github(*args):
            return json.dumps(info) if args[0] == 'gh' else real_output(*args)
        return remote, info, with_fake_github

    def test_successful_merge_pushes_exact_validated_commit(self):
        policy = self.propose('docs/usage.md', 'routine docs\n')
        remote, _, fake = self.prepare_remote(policy)
        with patch.object(merge_tool, 'output', side_effect=fake):
            self.assertTrue(merge_tool.merge(policy, 'master', 'codex/upstream-sync', 'test-pr'))
        self.assertEqual(self.git('--git-dir', str(remote), 'rev-parse', 'master'), policy['head'])

    def test_manual_policy_does_not_even_contact_github(self):
        with patch.object(merge_tool, 'output', side_effect=AssertionError('Must not attempt a merge')):
            self.assertFalse(merge_tool.merge({'routine': False}, 'master', 'codex/upstream-sync', 'test-pr'))

    def test_changed_default_branch_blocks_merge(self):
        policy = self.propose('docs/usage.md', 'routine docs\n')
        remote, _, fake = self.prepare_remote(policy)
        self.git('switch', '--detach', self.base)
        self.write('new-default.txt', 'new fork commit\n')
        self.git('add', '.')
        self.git('commit', '-m', 'Default changed while checks ran')
        newer = self.git('rev-parse', 'HEAD')
        self.git('push', 'origin', f'{newer}:refs/heads/master')
        self.git('switch', '--detach', policy['head'])
        with patch.object(merge_tool, 'output', side_effect=fake):
            with self.assertRaisesRegex(ValueError, 'changed while checks ran'):
                merge_tool.merge(policy, 'master', 'codex/upstream-sync', 'test-pr')
        self.assertEqual(self.git('--git-dir', str(remote), 'rev-parse', 'master'), newer)

    def test_changed_pr_identity_blocks_merge(self):
        policy = self.propose('docs/usage.md', 'routine docs\n')
        remote, info, fake = self.prepare_remote(policy)
        info['author']['login'] = 'another-author'
        with patch.object(merge_tool, 'output', side_effect=fake):
            with self.assertRaisesRegex(ValueError, 'identity or commit changed'):
                merge_tool.merge(policy, 'master', 'codex/upstream-sync', 'test-pr')
        self.assertEqual(self.git('--git-dir', str(remote), 'rev-parse', 'master'), policy['base'])

    def test_changed_update_branch_blocks_merge(self):
        policy = self.propose('docs/usage.md', 'routine docs\n')
        remote, _, fake = self.prepare_remote(policy)
        self.write('docs/later.md', 'branch changed after checks\n')
        self.git('add', '.')
        self.git('commit', '-m', 'Another update branch push')
        self.git('push', 'origin', 'HEAD:refs/heads/codex/upstream-sync')
        self.git('switch', '--detach', policy['head'])
        with patch.object(merge_tool, 'output', side_effect=fake):
            with self.assertRaisesRegex(ValueError, 'changed while checks ran'):
                merge_tool.merge(policy, 'master', 'codex/upstream-sync', 'test-pr')
        self.assertEqual(self.git('--git-dir', str(remote), 'rev-parse', 'master'), policy['base'])

    def test_concurrent_push_after_last_check_is_rejected_atomically(self):
        policy = self.propose('docs/usage.md', 'routine docs\n')
        remote, _, fake = self.prepare_remote(policy)
        self.git('switch', '--detach', self.base)
        self.write('race.txt', 'concurrent fork change\n')
        self.git('add', '.')
        self.git('commit', '-m', 'Concurrent fork commit')
        newer = self.git('rev-parse', 'HEAD')
        self.git('switch', '--detach', policy['head'])

        def race_after_pr_check(*args):
            result = fake(*args)
            if args[0] == 'gh':
                self.git('push', 'origin', f'{newer}:refs/heads/master')
            return result
        with patch.object(merge_tool, 'output', side_effect=race_after_pr_check):
            with self.assertRaises(subprocess.CalledProcessError):
                merge_tool.merge(policy, 'master', 'codex/upstream-sync', 'test-pr')
        self.assertEqual(self.git('--git-dir', str(remote), 'rev-parse', 'master'), newer)


if __name__ == '__main__':
    unittest.main()
