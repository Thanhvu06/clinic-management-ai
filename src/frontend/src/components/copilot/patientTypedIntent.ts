export type PatientTypedIntent = 'booking' | 'appointments' | 'visits' | 'prescriptions' | 'results' | 'bills';

// Short tokens must match exactly; longer keywords tolerate one edit.
function matchesToken(token: string, keyword: string): boolean {
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

export function classifyPatientTypedIntent(text: string): PatientTypedIntent | null {
    if (text.length > 120) return null;
    const normalized = text.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/[đĐ]/g, 'd')
        .toLowerCase().replace(/[^a-z0-9\s]/g, ' ').trim().replace(/\s+/g, ' ');
    const tokens = normalized ? normalized.split(' ') : [];
    // Keep the question word "đâu" distinct from the pain keyword "đau".
    const originalTokens = text.normalize('NFC').toLowerCase().replace(/[^\p{L}\p{N}\s]/gu, ' ').trim().split(/\s+/);
    const has = (phrase: string, fuzzy = true) => {
        const keywords = phrase.split(' ');
        return tokens.some((_, start) => !(phrase === 'dau' && originalTokens[start] === 'đâu') && keywords.every((keyword, offset) =>
            tokens[start + offset] !== undefined && (fuzzy ? matchesToken(tokens[start + offset], keyword) : tokens[start + offset] === keyword)));
    };
    const any = (phrases: string[]) => phrases.some(phrase => has(phrase));

    // Guards use whole words, avoiding false positives such as "sàng" -> "sưng".
    if (['dau nguc', 'kho tho', 'ngat', 'bat tinh', 'co giat', 'chay mau', 'dot quy', 'liet', '115',
        'dau', 'sot', 'ho', 'met', 'chong mat', 'buon non', 'non', 'ngua', 'sung', 'tieu chay', 'tuc nguc', 'nhuc dau']
        .some(phrase => has(phrase, false))) return null;

    if (['huy', 'hoan', 'khong den'].some(phrase => has(phrase, false)) ||
        (has('doi', false) && ['lich', 'gio', 'ngay', 'sang'].some(phrase => has(phrase, false)))) return null;
    if (['gia', 'bao nhieu', 'o dau', 'the nao', 'nhu the nao', 'cach', 'mo cua', 'gio lam viec', 'dia chi']
        .some(phrase => has(phrase, false))) return null;

    // Explicit booking wins over the doctor/specialty schedule-question guard.
    if (any(['dat lich', 'dat kham', 'dang ky kham', 'muon kham', 'hen kham', 'book lich'])) return 'booking';
    if ((has('bac si', false) || has('khoa', false)) && (has('lich', false) || has('kham', false))) return null;
    if (any(['lich hen', 'lich kham', 'lich cua toi'])) return 'appointments';
    if (any(['luot kham', 'lich su kham'])) return 'visits';
    if (any(['don thuoc', 'toa thuoc'])) return 'prescriptions';
    if (has('ket qua') && (any(['xet nghiem', 'can lam sang', 'sieu am', 'chup']) ||
        (has('cua toi') && !has('kham benh')))) return 'results';
    if (any(['hoa don', 'bien lai', 'vien phi'])) return 'bills';
    return null;
}
