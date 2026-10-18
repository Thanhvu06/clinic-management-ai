import { execFileSync } from 'node:child_process';

const protectedDirectories = [
    'src/backend/ClinicManagement.Infrastructure/AI/',
    'src/backend/ClinicManagement.Application/AI/',
    'src/tools/', 'docs/ai/', '.github/', 'scripts/e2e/', 'scripts/guard/',
];
const protectedFiles = new Set([
    'src/frontend/src/contexts/ChatContext.tsx',
    'src/frontend/src/hooks/useAiBookingFlow.ts',
    'src/frontend/src/api/aiCopilotApi.ts',
    'src/frontend/package.json', 'src/frontend/package-lock.json',
    'src/frontend/vite.config.ts', 'src/frontend/.oxlintrc.json',
]);
const forbiddenAntd = new Set(['Row', 'Col', 'Grid', 'Tabs', 'List', 'DatePicker']);

function isProtected(path) {
    return protectedFiles.has(path) || protectedDirectories.some(prefix => path.startsWith(prefix)) ||
        /^src\/frontend\/src\/components\/copilot\/[^/]+$/.test(path) ||
        /^src\/frontend\/src\/components\/MedicalChatWidget[^/]*$/.test(path);
}

function isTest(path) {
    return /\.test\.(?:ts|tsx|mjs)$/.test(path) ||
        /^src\/backend\/(?:[^/]+\/)*[^/]*Tests[^/]*\/(?:[^/]+\/)*[^/]+\.cs$/.test(path);
}

function changesFrom(output) {
    const fields = output.split('\0');
    const changes = [];
    for (let index = 0; index < fields.length - 1;) {
        const status = fields[index++];
        const oldPath = fields[index++];
        const path = /^[RC]/.test(status) ? fields[index++] : oldPath;
        changes.push({ status: status[0], oldPath, path });
    }
    return changes;
}

function addedLines(patch) {
    const lines = [];
    let number = null;
    for (const line of patch.split('\n')) {
        const hunk = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@/.exec(line);
        if (hunk) number = Number(hunk[1]);
        else if (line.startsWith('diff --git ')) number = null;
        else if (number !== null && line.startsWith('+')) lines.push({ number: number++, text: line.slice(1) });
        else if (number !== null && line.startsWith(' ')) number++;
    }
    return lines;
}

function tokensFrom(source) {
    const tokens = [];
    const pattern = /\s+|\/\/[^\n]*|\/\*[\s\S]*?\*\/|"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'|`(?:\\[\s\S]|[^`\\])*`|[A-Za-z_$][\w$]*|[^\s]/g;
    for (const match of source.matchAll(pattern)) {
        const value = match[0];
        if (/^\s|^\/\/|^\/\*/.test(value)) continue;
        const quoted = /^["'`]/.test(value);
        tokens.push({ value: quoted ? value.slice(1, -1) : value, quoted, offset: match.index });
    }
    return tokens;
}

function antdImports(source) {
    const tokens = tokensFrom(source);
    const imports = new Map();
    for (let index = 0; index < tokens.length; index++) {
        if (tokens[index].value !== 'import' || tokens[index].quoted || tokens[index - 1]?.value === '.') continue;
        let cursor = index + 1;
        if (tokens[cursor]?.value === 'type') cursor++;
        if (tokens[cursor]?.value !== '{' && tokens[cursor + 1]?.value === ',') cursor += 2;
        if (tokens[cursor]?.value !== '{') continue;
        const start = ++cursor;
        while (cursor < tokens.length && tokens[cursor].value !== '}') cursor++;
        if (tokens[cursor + 1]?.value !== 'from' || !tokens[cursor + 2]?.quoted || tokens[cursor + 2].value !== 'antd') continue;
        for (let item = start; item < cursor; item++) {
            if (item !== start && tokens[item - 1].value !== ',') continue;
            if (tokens[item].value === 'type' && tokens[item + 1]?.value !== 'as') item++;
            const token = tokens[item];
            if (token && !token.quoted) imports.set(token.value, source.slice(0, token.offset).split('\n').length);
        }
        index = cursor;
    }
    return imports;
}

function lineCount(source) {
    if (!source) return 0;
    return source.split('\n').length - (source.endsWith('\n') ? 1 : 0);
}

function hexColors(source) {
    return [...source.matchAll(/(?<![\p{L}\p{N}])#(?:[\da-f]{8}|[\da-f]{6}|[\da-f]{4}|[\da-f]{3})(?![\p{L}\p{N}])/giu)]
        .map(match => match[0].toLowerCase());
}

function parseOptions(args) {
    const options = { labels: '' };
    for (let index = 0; index < args.length; index += 2) {
        const name = args[index];
        if (!['--base', '--head', '--labels'].includes(name) || args[index + 1] === undefined) {
            throw new Error('Usage: node scripts/guard/pr-guard.mjs --base <sha> --head <sha> --labels "label1,label2"');
        }
        options[name.slice(2)] = args[index + 1];
    }
    if (!options.base || !options.head) throw new Error('--base and --head are required.');
    return options;
}

function main() {
    const options = parseOptions(process.argv.slice(2));
    const git = (...args) => execFileSync('git', ['--literal-pathspecs', '-c', 'core.quotepath=false', ...args], {
        encoding: 'utf8', maxBuffer: 64 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'],
    });
    process.chdir(git('rev-parse', '--show-toplevel').trim());
    const base = git('rev-parse', '--verify', '--end-of-options', `${options.base}^{commit}`).trim();
    const head = git('rev-parse', '--verify', '--end-of-options', `${options.head}^{commit}`).trim();
    const labels = new Set(options.labels.split(',').map(label => label.trim()).filter(Boolean));
    const range = `${base}...${head}`;
    const changes = changesFrom(git('diff', '--no-ext-diff', '--no-textconv', '--name-status', '-z', '-M', range, '--'));
    const violations = [];
    const report = (path, line, code, message) => violations.push({ path, line, code, message });
    const readAt = (revision, path) => {
        const files = git('ls-tree', '-z', '--name-only', revision, '--', path).split('\0');
        return files.includes(path) ? git('show', `${revision}:${path}`) : '';
    };

    for (const change of changes) {
        const { path, oldPath, status } = change;
        if (!labels.has('allow-protected') && [oldPath, path].some(isProtected)) {
            report(path, 1, 'G1 PROTECTED_PATH', 'Protected path changed; requires allow-protected.');
        }
        if (!labels.has('allow-test-changes') && ['M', 'D', 'R'].includes(status) && isTest(oldPath) &&
            git('ls-tree', '-z', '--name-only', base, '--', oldPath).split('\0').includes(oldPath)) {
            report(path, 1, 'G2 OLD_TEST_CHANGED', 'Existing test modified, deleted or renamed; requires allow-test-changes.');
        }
        if (status === 'D') continue;

        const previous = status === 'A' ? '' : readAt(base, oldPath);
        const current = readAt(head, path);
        if (/\.(?:ts|tsx|cs)$/.test(path) && lineCount(previous) > 1000 && lineCount(current) > lineCount(previous) &&
            !labels.has('allow-large-file')) {
            report(path, 1001, 'G7 LARGE_FILE', `File grew from ${lineCount(previous)} to ${lineCount(current)} lines; requires allow-large-file.`);
        }

        const paths = [...new Set([oldPath, path])];
        const stats = git('diff', '--no-ext-diff', '--no-textconv', '--numstat', '-z', '-M', range, '--', ...paths);
        if (stats.startsWith('-\t-\t') || current.includes('\0') || previous.includes('\0')) continue;
        const patch = git('diff', '--no-ext-diff', '--no-textconv', '--no-color', '-U0', '-M', range, '--', ...paths);
        const existingColors = new Set(hexColors(previous));
        for (const line of addedLines(patch)) {
            if (/\.(?:skip|only)\(|\b(?:xit|xdescribe)\(|\[(?:Fact|Theory)\(Skip/.test(line.text)) {
                report(path, line.number, 'G3 TEST_SKIP', 'Added disabled or exclusive test syntax; no label can allow this.');
            }
            if (isTest(path) || !path.startsWith('src/frontend/src/')) continue;
            if (/\.(?:ts|tsx|css)$/.test(path) && path !== 'src/frontend/src/index.css' &&
                hexColors(line.text).some(color => !existingColors.has(color))) {
                report(path, line.number, 'G4 HEX_COLOR', 'Added hex color; use --cc-* tokens.');
            }
            if (path.endsWith('.tsx') && /style=\{\{/.test(line.text)) {
                report(path, line.number, 'G5 INLINE_STYLE', 'Added inline style; use a CSS class.');
            }
        }
        if (path.endsWith('.tsx') && !isTest(path)) {
            const before = antdImports(previous);
            for (const [name, line] of antdImports(current)) {
                if (forbiddenAntd.has(name) && !before.has(name)) {
                    report(path, line, 'G6 FORBIDDEN_ANTD', `New antd import ${name}; use an allowed component.`);
                }
            }
        }
    }
    violations.sort((a, b) => a.path.localeCompare(b.path) || a.line - b.line || a.code.localeCompare(b.code));
    for (const item of violations) console.log(`${item.path}:${item.line}  [${item.code}]  ${item.message}`);
    console.log(`PR Guard: ${violations.length} violation(s).`);
    process.exitCode = violations.length ? 1 : 0;
}

try {
    main();
} catch (error) {
    console.error(`PR Guard error: ${error.message}`);
    process.exitCode = 2;
}
