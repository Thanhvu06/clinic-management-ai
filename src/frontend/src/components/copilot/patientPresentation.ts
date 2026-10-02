import type { AiSuggestionItem, ChatMessage } from '../../types/ai';

export const isAdditionalCopy = (text: string | null | undefined, message: string): boolean => Boolean(text &&
    text.trim().replace(/\s+/g, ' ').toLocaleLowerCase('vi-VN') !== message.trim().replace(/\s+/g, ' ').toLocaleLowerCase('vi-VN'));

export const isLocalHelpPhrase = (value: string): boolean => /^(?:ban lam duoc gi|ban co the lam gi|lam duoc gi|co the lam gi|giup gi|huong dan|menu|tro giup|toi co quyen han gi|toi lam duoc gi|cach dung)[!.?]*$/.test(
    value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase().replace(/\s+/g, ' ').trim());

// Routing only: the server still resolves the phrase, actor and authorized read.
// These exact patient phrases keep legacy booking/clinical turns on their path.
export const isPatientReadAlias = (value: string): boolean => [
    'lich hen cua minh', 'lich cua toi', 'cac luot kham cua toi', 'lich su kham cua minh',
    'ket qua xet nghiem cua minh', 'xem ket qua cua toi', 'toa thuoc cua minh', 'xem don thuoc cua toi',
    'bien lai cua toi', 'hoa don cua minh'
].includes(value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/Đ/g, 'D').toLowerCase().replace(/\s+/g, ' ').trim().replace(/[!.?]+$/, ''));

export const patientSuggestions = (message: ChatMessage | undefined, menu: AiSuggestionItem[] | undefined, empty: boolean): AiSuggestionItem[] | undefined => {
    const items = empty ? menu : message?.suggestionChips ?? menu;
    const start = menu?.find(item => item.code === 'patient.start_booking');
    const needsBooking = message?.missingFields?.some(field => /doctor|slot|reason/i.test(field)) || message?.bookingDraft && !message.bookingDraft.isComplete;
    return needsBooking && start ? [start, ...(items ?? []).filter(item => item.code !== start.code)].slice(0, 6) : items;
};
