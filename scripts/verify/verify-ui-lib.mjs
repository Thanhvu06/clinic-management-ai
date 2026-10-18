const rolePrefixes = [
    ['/patient', 'patient'], ['/reception', 'receptionist'], ['/doctor', 'doctor'],
    ['/diagnostics', 'technician'], ['/pharmacy', 'pharmacist'], ['/admin', 'admin']
];

export const codes = {
    V1: 'CONSOLE_ERROR', V2: 'HTTP_5XX', V3: 'BLANK', V4: 'CRASH', V5: 'H_OVERFLOW'
};

export function matchesPrefix(route, prefix) {
    const normalized = prefix.replace(/\/$/, '') || '/';
    return normalized === '/' || route === normalized || route.startsWith(`${normalized}/`);
}

export function roleForRoute(route) {
    if (route === '/notifications') return 'receptionist';
    return rolePrefixes.find(([prefix]) => matchesPrefix(route, prefix))?.[1] ?? 'public';
}

function routeTags(source) {
    const tags = [];
    const pattern = /<\/?Route\b/g;
    let match;
    while ((match = pattern.exec(source))) {
        let braces = 0;
        let quote = '';
        let end = pattern.lastIndex;
        for (; end < source.length; end++) {
            const char = source[end];
            if (quote) {
                if (char === '\\') end++;
                else if (char === quote) quote = '';
            } else if ('"\'`'.includes(char)) quote = char;
            else if (char === '{') braces++;
            else if (char === '}') braces--;
            else if (char === '>' && braces === 0) break;
        }
        tags.push(source.slice(match.index, end + 1));
        pattern.lastIndex = end + 1;
    }
    return tags;
}

export function parseRoutes(source) {
    const routes = [];
    const skipped = [];
    const seen = new Set();
    const parents = ['/'];
    const uncommented = source.replace(/\{\/\*[\s\S]*?\*\/\}/g, '').replace(/\/\*[\s\S]*?\*\//g, '');
    for (const tag of routeTags(uncommented)) {
        if (tag.startsWith('</')) {
            parents.pop();
            continue;
        }
        const literal = tag.match(/\bpath\s*=\s*(?:["']([^"']*)["']|\{\s*["']([^"']*)["']\s*\})/);
        const path = literal?.[1] ?? literal?.[2];
        const parent = parents.at(-1) ?? '/';
        const route = path === undefined ? parent : path.startsWith('/') ? path : `${parent.replace(/\/$/, '')}/${path}`;
        if (!tag.endsWith('/>')) parents.push(route);
        if (path === undefined && !/\bindex(?:\s|=|\/|>)/.test(tag)) continue;
        if (seen.has(route)) continue;
        seen.add(route);
        let reason;
        if (route.includes(':')) reason = 'Có tham số';
        else if (route.includes('*') || route === '/404' || /\bNotFoundPage\b/.test(tag)) reason = 'Trang 404 / wildcard';
        else if (['/login', '/register'].includes(route)) reason = 'Trang đăng nhập / đăng ký';
        else if (matchesPrefix(route, '/lab')) reason = 'Thiếu tài khoản LabSupervisor';
        if (reason) skipped.push({ route, reason });
        else routes.push({ route, role: roleForRoute(route) });
    }
    return { routes, skipped };
}

export function routeSlug(route) {
    return route.replace(/[^a-zA-Z0-9_-]+/g, '-').replace(/^-+|-+$/g, '') || 'home';
}

export function validateBaseline(baseline) {
    if (!Array.isArray(baseline) || baseline.some(issue =>
        typeof issue.route !== 'string' || !['desktop', 'mobile'].includes(issue.viewport) ||
        !Object.hasOwn(codes, issue.code) || typeof issue.match !== 'string' || !issue.match.trim())) {
        throw new Error('Invalid baseline: expected { route, viewport: desktop|mobile, code: V1..V5, match: nonempty string }[].');
    }
    return baseline;
}

export function classifyBaseline(results, baseline, scope) {
    validateBaseline(baseline);
    const matched = new Set();
    const rows = results.map(result => {
        const issues = result.issues.map(issue => {
            const matches = baseline.flatMap((known, index) =>
                known.route === result.route && known.viewport === result.viewport && known.code === issue.code &&
                issue.message.includes(known.match) ? [index] : []);
            matches.forEach(index => matched.add(index));
            return { ...issue, status: matches.length ? 'KNOWN' : 'NEW' };
        });
        return { ...result, issues, status: issues.some(issue => issue.status === 'NEW') ? 'NEW' : issues.length ? 'KNOWN' : 'PASS' };
    });
    const selected = new Set(results.map(result => `${result.route}\n${result.viewport}`));
    const inScope = issue => scope
        ? scope.viewports.includes(issue.viewport) && (!scope.only.length || scope.only.some(prefix => matchesPrefix(issue.route, prefix)))
        : selected.has(`${issue.route}\n${issue.viewport}`);
    const stale = baseline.filter((issue, index) => inScope(issue) && !matched.has(index));
    return { results: rows, stale, hasNew: rows.some(row => row.status === 'NEW') };
}

export function parseOptions(args) {
    const options = { only: [], viewports: ['desktop', 'mobile'], skipBuild: false };
    for (let index = 0; index < args.length; index++) {
        const arg = args[index];
        if (arg === '--skip-build') options.skipBuild = true;
        else if (arg === '--only') {
            const prefix = args[++index];
            if (!prefix?.startsWith('/')) throw new Error('--only requires a route prefix starting with /.');
            options.only.push(prefix);
        } else if (arg === '--viewport') {
            const viewport = args[++index];
            if (!['desktop', 'mobile'].includes(viewport)) throw new Error('--viewport requires desktop or mobile.');
            options.viewports = [viewport];
        } else throw new Error(`Unknown argument: ${arg}`);
    }
    return options;
}
