import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const guard = fileURLToPath(new URL('./pr-guard.mjs', import.meta.url));
const frontend = 'src/frontend/src/pages/Example.tsx';
const allLabels = 'allow-protected,allow-test-changes,allow-large-file';

function scenario(t, baseFiles = {}, headFiles = {}, renames = []) {
    const tempRoot = resolve(tmpdir());
    const repo = mkdtempSync(join(tempRoot, 'clinic-pr-guard-'));
    t.after(() => {
        assert.equal(dirname(repo), tempRoot);
        rmSync(repo, { recursive: true, force: true });
    });
    const git = (...args) => execFileSync('git', ['-c', 'core.autocrlf=false', '-c', 'commit.gpgsign=false', ...args], {
        cwd: repo, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
    }).trim();
    git('init', '--quiet');
    git('config', 'user.name', 'PR Guard Test');
    git('config', 'user.email', 'pr-guard@synthetic.invalid');
    git('config', 'core.hooksPath', join(repo, 'no-hooks'));
    const writeFiles = files => {
        for (const [path, content] of Object.entries(files)) {
            const target = join(repo, path);
            if (content === null) rmSync(target);
            else {
                mkdirSync(dirname(target), { recursive: true });
                writeFileSync(target, content);
            }
        }
    };
    const commit = message => {
        git('add', '--all');
        git('commit', '--quiet', '--allow-empty', '-m', message);
        return git('rev-parse', 'HEAD');
    };
    writeFiles(baseFiles);
    const base = commit('base');
    for (const [from, to] of renames) {
        mkdirSync(dirname(join(repo, to)), { recursive: true });
        renameSync(join(repo, from), join(repo, to));
    }
    writeFiles(headFiles);
    const head = commit('head');
    return (labels = '', options = {}) => {
        const result = spawnSync(process.execPath, [guard, '--base', options.base ?? base, '--head', options.head ?? head, '--labels', labels], {
            cwd: options.cwd ?? repo, encoding: 'utf8',
        });
        assert.ifError(result.error);
        return { status: result.status, output: result.stdout + result.stderr, git, repo, base, head };
    };
}

function expectCodes(result, codes) {
    assert.equal(result.status, codes.length ? 1 : 0, result.output);
    const actual = [...result.output.matchAll(/\[(G\d) [A-Z_]+\]/g)].map(match => match[1]);
    assert.deepEqual(actual.sort(), [...codes].sort(), result.output);
}

test('G1 blocks every protected path family and allows the named label', t => {
    const paths = [
        'src/backend/ClinicManagement.Infrastructure/AI/nested/Service.cs',
        'src/backend/ClinicManagement.Application/AI/Service.cs',
        'src/frontend/src/components/copilot/Panel.tsx',
        'src/frontend/src/components/MedicalChatWidget.tsx',
        'src/frontend/src/components/MedicalChatWidget.module.css',
        'src/frontend/src/contexts/ChatContext.tsx',
        'src/frontend/src/hooks/useAiBookingFlow.ts',
        'src/frontend/src/api/aiCopilotApi.ts',
        'src/tools/nested/data.json', 'docs/ai/guide.md', '.github/workflows/example.yml',
        'scripts/e2e/example.mjs', 'scripts/guard/example.mjs',
        'src/frontend/package.json', 'src/frontend/package-lock.json',
        'src/frontend/vite.config.ts', 'src/frontend/.oxlintrc.json',
    ];
    const run = scenario(t, {}, Object.fromEntries(paths.map(path => [path, 'content\n'])));
    const blocked = run();
    expectCodes(blocked, paths.map(() => 'G1'));
    for (const path of paths) assert.ok(blocked.output.includes(`${path}:1  [G1 PROTECTED_PATH]`));
    expectCodes(run('allow-protected'), []);
    expectCodes(run('allow-test-changes,allow-large-file'), paths.map(() => 'G1'));
});

test('G1 allows AGENTS and nearby paths outside the protected glob boundaries', t => {
    const paths = ['AGENTS.md', 'docs/aide.md', 'scripts/guards/a.mjs',
        'src/frontend/src/components/copilot/nested/Helper.tsx',
        'src/frontend/src/components/OtherMedicalChatWidget.tsx'];
    expectCodes(scenario(t, {}, Object.fromEntries(paths.map(path => [path, 'safe\n'])))(), []);
});

test('G1 covers modified, deleted and renamed protected paths in either direction', t => {
    const run = scenario(t, {
        'docs/ai/modify.md': 'old\n', 'docs/ai/delete.md': 'delete\n',
        'docs/ai/leave.md': 'leaving protected area\n', 'safe.md': 'entering protected area\n',
    }, { 'docs/ai/modify.md': 'new\n', 'docs/ai/delete.md': null }, [
        ['docs/ai/leave.md', 'moved.md'], ['safe.md', 'docs/ai/enter.md'],
    ]);
    expectCodes(run(), ['G1', 'G1', 'G1', 'G1']);
    expectCodes(run('allow-protected'), []);
});

for (const path of ['src/frontend/src/example.test.ts', 'src/frontend/src/example.test.tsx',
    'scripts/example.test.mjs', 'src/backend/ClinicManagement.IntegrationTests/Nested/Example.cs']) {
    test(`G2 blocks an existing modified test: ${path}`, t => {
        const run = scenario(t, { [path]: 'old\n' }, { [path]: 'new\n' });
        expectCodes(run(), ['G2']);
        expectCodes(run('allow-test-changes'), []);
        expectCodes(run('allow-protected,allow-large-file'), ['G2']);
    });
}

test('G2 blocks deleted tests and renames out of a test filename', t => {
    const run = scenario(t, { 'old.test.ts': 'removed\n', 'rename.test.mjs': 'renamed test\n' },
        { 'old.test.ts': null }, [['rename.test.mjs', 'renamed.mjs']]);
    expectCodes(run(), ['G2', 'G2']);
    expectCodes(run('allow-test-changes'), []);
});

test('G2 permits new tests and ordinary changes without labels', t => {
    expectCodes(scenario(t, { 'readme.md': 'old\n' }, {
        'new.test.ts': 'test\n', 'new.test.tsx': 'test\n', 'new.test.mjs': 'test\n',
        'src/backend/UnitTests/Nested/New.cs': 'test\n', 'readme.md': 'new\n',
    })(), []);
});

// Assemble fixture syntax at runtime so this test file obeys G3 itself.
const testCalls = [
    ...['skip', 'only'].map(method => `it.${method}('case', () => {});`),
    ...['xit', 'xdescribe'].map(name => `${name}('case', () => {});`),
    ...['Fact', 'Theory'].map(name => `[${name}(Skip = "reason")]`),
];
for (const [index, syntax] of testCalls.entries()) {
    test(`G3 blocks disabled/exclusive syntax variant ${index + 1} even with all labels`, t => {
        const run = scenario(t, {}, { 'example.test.mjs': `${syntax}\n` });
        const blocked = run();
        expectCodes(blocked, ['G3']);
        expectCodes(run(allLabels), ['G3']);
        assert.match(blocked.output, /example\.test\.mjs:1  \[G3 TEST_SKIP\]/);
    });
}

test('G3 ignores ordinary tests and unchanged legacy disabled syntax', t => {
    const run = scenario(t, { 'legacy.mjs': `${testCalls[0]}\nold\n` }, {
        'legacy.mjs': `${testCalls[0]}\nnew\n`, 'new.test.mjs': "test('ordinary', () => {});\n",
    });
    expectCodes(run(), []);
});

for (const extension of ['ts', 'tsx', 'css']) {
    test(`G4 detects added hex colors in ${extension} with exact head line numbers`, t => {
        const path = `src/frontend/src/example.${extension}`;
        const run = scenario(t, {}, { [path]: "first\n'#fff'\n\"#1677ff\"\ncolor: #abc;\n'#aBcD'\n'#12345678'\n" });
        const blocked = run();
        expectCodes(blocked, ['G4', 'G4', 'G4', 'G4', 'G4']);
        for (const line of [2, 3, 4, 5, 6]) assert.ok(blocked.output.includes(`${path}:${line}  [G4 HEX_COLOR]`));
        expectCodes(run(allLabels), ['G4', 'G4', 'G4', 'G4', 'G4']);
    });
}

test('G4 permits anchors, tokens, non-color suffixes, index.css and test fixtures', t => {
    expectCodes(scenario(t, {}, {
        [frontend]: "const values = ['#section', 'word#fff', '9#abc', '#abcdex', '#12', '#12345', '#1234567', '#123456789', 'var(--cc-color-primary)'];\n",
        'src/frontend/src/index.css': ':root { --cc-test: #fff; }\n',
        'src/frontend/src/example.test.tsx': 'const color = "#fff";\n',
    })(), []);
});

test('G4 and G5 inspect only added lines, ignoring legacy violations in a modified file', t => {
    const legacy = 'const view = <div style={{ color: "#fff" }} />;\n';
    expectCodes(scenario(t, { [frontend]: `${legacy}const before = 1;\n` }, {
        [frontend]: `${legacy}const after = 2;\n`,
    })(), []);
});

test('G4 permits reformatting or moving a hex color already present at base, regardless of case', t => {
    expectCodes(scenario(t, { [frontend]: 'const legacy = "#ABC";\nconst value = 1;\n' }, {
        [frontend]: 'const value = 2;\nconst reformatted = "#abc";\nconst reused = "#AbC";\n',
    })(), []);
});

test('G4 still rejects a new hex beside legacy colors and distinguishes complete color tokens', t => {
    const run = scenario(t, { [frontend]: 'const legacy = "#fff0";\n' }, {
        [frontend]: 'const values = ["#fff0", "#fff"];\nconst other = "#123456";\n',
    });
    expectCodes(run(), ['G4', 'G4']);
});

test('G4 and G5 preserve line numbers across multiple diff hunks and missing final newline', t => {
    const run = scenario(t, { [frontend]: 'a\nb\nc\nd\ne\n' }, {
        [frontend]: 'a\nconst color = "#fff";\nb\nc\nd\n<div style={{}} />',
    });
    const blocked = run();
    expectCodes(blocked, ['G4', 'G5']);
    assert.ok(blocked.output.includes(`${frontend}:2  [G4 HEX_COLOR]`));
    assert.ok(blocked.output.includes(`${frontend}:6  [G5 INLINE_STYLE]`));
});

test('G5 blocks a newly added inline style even with all labels', t => {
    const run = scenario(t, {}, { [frontend]: '<div style={{ padding: 1 }} />;\n' });
    expectCodes(run(), ['G5']);
    expectCodes(run(allLabels), ['G5']);
});

test('G5 permits classes, test fixtures and files outside frontend TSX', t => {
    expectCodes(scenario(t, {}, {
        [frontend]: '<div className={styles.panel} />;\n',
        'src/frontend/src/example.test.tsx': '<div style={{}} />;\n',
        'src/frontend/src/example.ts': 'const fixture = "style={{";\n',
    })(), []);
});

test('G6 detects all forbidden named imports including multiline imports and aliases', t => {
    const run = scenario(t, {}, { [frontend]: "import {\n  Row as AntRow,\n  Col, Grid,\n  Tabs, List, DatePicker\n} from 'antd';\n" });
    const blocked = run();
    expectCodes(blocked, Array(6).fill('G6'));
    assert.ok(blocked.output.includes(`${frontend}:2  [G6 FORBIDDEN_ANTD]  New antd import Row;`));
    expectCodes(run(allLabels), Array(6).fill('G6'));
});

test('G6 compares imported names rather than aliases and ignores existing imports', t => {
    const run = scenario(t, { [frontend]: "import { Row as Before } from 'antd';\n" }, {
        [frontend]: "import {\n Row as After, Button as List, Flex\n} from 'antd';\n",
    });
    expectCodes(run(), []);
});

test('G6 detects a new forbidden import in a modified file with a default binding', t => {
    expectCodes(scenario(t, { [frontend]: "import { Button } from 'antd';\n" }, {
        [frontend]: "import antd, { Button, Row as LayoutRow } from 'antd';\n",
    })(), ['G6']);
});

test('G6 ignores comments, strings, other modules, removed names and test files', t => {
    expectCodes(scenario(t, { [frontend]: "import { Row } from 'antd';\n" }, {
        [frontend]: '// import { List } from "antd";\n/* import { Tabs } from "antd"; */\n' +
            'const text = "import { Grid } from \'antd\';";\n' + "import { DatePicker } from 'other';\nimport { Button } from 'antd';\n",
        'src/frontend/src/example.test.tsx': "import { Row } from 'antd';\n",
    })(), []);
});

test('G6 handles type specifiers, type imports and whitespace in the module declaration', t => {
    expectCodes(scenario(t, {}, { [frontend]: 'import type { Row as RowType } from "antd";\nimport { type Col as ColType } from "antd";\n' })(), ['G6', 'G6']);
});

for (const extension of ['ts', 'tsx', 'cs']) {
    test(`G7 rejects growth of an existing file over 1000 lines: ${extension}`, t => {
        const path = `large.${extension}`;
        const base = 'line\n'.repeat(1001);
        const run = scenario(t, { [path]: base }, { [path]: `${base}added\n` });
        expectCodes(run(), ['G7']);
        expectCodes(run('allow-large-file'), []);
        expectCodes(run('allow-protected,allow-test-changes'), ['G7']);
    });
}

test('G7 allows new large files, a 1000-line base, non-code files and non-growing edits', t => {
    const base = 'line\n'.repeat(1001);
    expectCodes(scenario(t, {
        'same.ts': base, 'smaller.cs': base, 'threshold.tsx': 'line\n'.repeat(1000), 'large.md': base,
    }, {
        'same.ts': base.replace('line', 'edit'), 'smaller.cs': 'line\n'.repeat(1000),
        'threshold.tsx': base, 'large.md': `${base}extra\n`, 'new.ts': base,
    })(), []);
});

test('G7 counts CRLF and an unterminated last line correctly, including renames', t => {
    const base = 'line\r\n'.repeat(1001);
    const run = scenario(t, { 'old.ts': base }, { 'new.ts': `${base}last` }, [['old.ts', 'new.ts']]);
    expectCodes(run(), ['G7']);
});

test('G3–G6 ignore binary and deleted files while G1/G2 still apply', t => {
    const bad = Buffer.from(`\0${testCalls[0]}\nimport { Row } from 'antd';\n<div style={{ color: '#fff' }} />\n`);
    const run = scenario(t, { 'old.test.tsx': 'existing test\n', [frontend]: 'old\n' }, {
        'old.test.tsx': null, [frontend]: bad,
        'src/frontend/src/components/MedicalChatWidget.tsx': bad,
    });
    expectCodes(run(), ['G1', 'G2']);
    expectCodes(run(allLabels), []);
});

test('Diff uses the merge base instead of changes made only on the target branch', t => {
    const run = scenario(t, { 'readme.md': 'base\n' }, { 'readme.md': 'head\n' });
    const context = run();
    context.git('checkout', '--quiet', '-b', 'target', context.base);
    writeFileSync(join(context.repo, 'target.test.ts'), 'only on target\n');
    context.git('add', '--all');
    context.git('commit', '--quiet', '-m', 'target branch');
    expectCodes(run('', { base: context.git('rev-parse', 'HEAD') }), []);
});

test('Diff handles spaces and Unicode paths and can run below the repository root', t => {
    const path = 'src/frontend/src/pages/Thử nghiệm với dấu.tsx';
    const run = scenario(t, {}, { [path]: '<div style={{}} />;\n' });
    const context = run();
    expectCodes(run('', { cwd: dirname(join(context.repo, path)) }), ['G5']);
    assert.ok(context.output.includes(`${path}:1  [G5 INLINE_STYLE]`));
});

test('Invalid revisions fail closed rather than passing the guard', t => {
    const run = scenario(t);
    const result = run('', { base: 'missing-revision' });
    assert.equal(result.status, 2);
    assert.match(result.output, /PR Guard error:/);
});
