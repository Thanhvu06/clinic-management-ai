// Older builds stored this sentence as soon as a consultation started; it is not a real conclusion.
export const LEGACY_SUMMARY_PLACEHOLDER = 'Đang trong quá trình thăm khám.';

export const isSummaryMissing = (summary: string | null | undefined): boolean => {
    const trimmed = (summary ?? '').trim();
    return trimmed === '' || trimmed.toLocaleLowerCase('vi') === LEGACY_SUMMARY_PLACEHOLDER.toLocaleLowerCase('vi');
};

export const loadedSummaryValue = (summary: string | null | undefined): string =>
    isSummaryMissing(summary) ? '' : summary ?? '';

export const completionFieldMissingMessage = (diagnosis: string, summary: string): string | null => {
    if (!diagnosis.trim()) return 'Vui lòng nhập chẩn đoán bệnh trước khi hoàn tất khám.';
    if (isSummaryMissing(summary)) return 'Vui lòng nhập tóm tắt kết luận khám trước khi hoàn tất.';
    return null;
};
