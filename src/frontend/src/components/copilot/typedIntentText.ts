// Shared, cosmetic text matching for typed chat routing. The server remains
// the authority: these helpers only choose which server-owned code to send.

/** Strips diacritics, lower-cases, keeps letters/digits and collapses whitespace. */
export function normalizeTypedText(text: string): string {
    return text.normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[đĐ]/g, 'd')
        .toLowerCase().replace(/[^a-z0-9\s]/g, ' ').trim().replace(/\s+/g, ' ');
}

export function typedTokens(text: string): string[] {
    const normalized = normalizeTypedText(text);
    return normalized ? normalized.split(' ') : [];
}

// Short tokens must match exactly; longer keywords tolerate one edit.
export function matchesToken(token: string, keyword: string): boolean {
    if (token === keyword) return true;
    if (Math.max(keyword.length, token.length) < 4 || Math.abs(token.length - keyword.length) > 1) return false;
    let previous = Array.from({ length: keyword.length + 1 }, (_, index) => index);
    for (let i = 1; i <= token.length; i++) {
        const current = [i];
        for (let j = 1; j <= keyword.length; j++) {
            current[j] = Math.min(current[j - 1] + 1, previous[j] + 1,
                previous[j - 1] + (token[i - 1] === keyword[j - 1] ? 0 : 1));
        }
        previous = current;
    }
    return previous[keyword.length] <= 1;
}

/** Index of the first token where every word of `phrase` matches in order, or -1. */
export function findPhrase(tokens: readonly string[], phrase: string, fuzzy = true, skip?: (start: number) => boolean): number {
    const keywords = phrase.split(' ');
    return tokens.findIndex((_, start) => !skip?.(start) && keywords.every((keyword, offset) =>
        tokens[start + offset] !== undefined && (fuzzy ? matchesToken(tokens[start + offset], keyword) : tokens[start + offset] === keyword)));
}
