import { findPhrase, normalizeTypedText, typedTokens } from './typedIntentText';

export type PatientTypedIntent = 'booking' | 'appointments' | 'visits' | 'prescriptions' | 'results' | 'bills';

export function classifyPatientTypedIntent(text: string): PatientTypedIntent | null {
    if (text.length > 120) return null;
    const tokens = typedTokens(text);
    // Keep the question word "đâu" distinct from the pain keyword "đau".
    const originalTokens = text.normalize('NFC').toLowerCase().replace(/[^\p{L}\p{N}\s]/gu, ' ').trim().split(/\s+/);
    const has = (phrase: string, fuzzy = true) =>
        findPhrase(tokens, phrase, fuzzy, start => phrase === 'dau' && originalTokens[start] === 'đâu') >= 0;
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

// Words around a typed doctor/specialty name that are not part of it. They
// are compared with their accents; unaccented forms are listed only when they
// cannot also start a name (so "tai" in "tai mũi họng" is kept).
const HINT_ANCHORS = ['bac si', 'bsi', 'bs', 'doctor', 'chuyen khoa', 'khoa', 'kham'];
const HINT_EDGE_WORDS = new Set(['với', 'voi', 'cho', 'tôi', 'toi', 'tối', 'mình', 'muốn', 'muon', 'giúp', 'giup', 'nhé', 'nhe', 'nha', 'ạ',
    'vào', 'vao', 'lúc', 'luc', 'ở', 'tại', 'đi', 'được', 'duoc', 'không', 'khong', 'nào', 'nao',
    'sđt', 'sdt', 'số', 'đặt', 'dat', 'lịch', 'lich', 'hẹn', 'hen', 'khám', 'kham', 'book', 'đăng', 'ký', 'em', 'con']);
const HINT_TIME_PHRASES = ['ngay mai', 'hom nay', 'tuan sau', 'tuan toi', 'tuan nay', 'sang mai', 'chieu mai', 'sang nay',
    'chieu nay', 'toi nay', 'cuoi tuan', 'ngay kia', 'som nhat', 'gan nhat', 'dien thoai'];
const MAX_HINT_WORDS = 6;

/**
 * Extracts a doctor or specialty name typed after "bác sĩ", "bs", "khoa",
 * "chuyên khoa" or "khám" (e.g. "đặt khám với bác sĩ Lan" -> "Lan"). Returns
 * null when nothing usable remains. Phone numbers, record codes and long
 * numbers are never included; the server matches the name itself.
 */
export function extractBookingHint(text: string): string | null {
    if (text.length > 120) return null;
    const cleaned = text.normalize('NFC')
        .replace(/\b[A-Za-z]{2,}-[A-Za-z0-9-]+\b/g, ' ')
        .replace(/\+?\d[\d\s.-]{4,}\d/g, ' ')
        .replace(/[^\p{L}\p{N}\s]/gu, ' ');
    const words = cleaned.trim().split(/\s+/).filter(word => word && !/\d/.test(word));
    const tokens = words.map(word => normalizeTypedText(word));

    let start = -1;
    for (const anchor of HINT_ANCHORS) {
        const index = findPhrase(tokens, anchor, false);
        if (index >= 0) { start = index + anchor.split(' ').length; break; }
    }
    if (start < 0) return null;

    let end = words.length;
    for (let index = start; index < words.length; index++) {
        if (HINT_ANCHORS.some(anchor => findPhrase(tokens.slice(index), anchor, false) === 0)) { end = index; break; }
        const time = HINT_TIME_PHRASES.find(phrase => findPhrase(tokens.slice(index), phrase, false) === 0);
        if (time) { end = index; break; }
    }
    let name = words.slice(start, end);
    const isEdge = (word: string) => HINT_EDGE_WORDS.has(word.toLowerCase());
    while (name.length && isEdge(name[0])) name = name.slice(1);
    while (name.length && isEdge(name[name.length - 1])) name = name.slice(0, -1);
    if (name.length === 0 || name.length > MAX_HINT_WORDS) return null;
    return name.join(' ');
}
