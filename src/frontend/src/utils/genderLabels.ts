export function getGenderLabel(gender?: string | number | null): string {
    switch (String(gender ?? '').trim().toLowerCase()) {
        case '0': case 'male': case 'nam': return 'Nam';
        case '1': case 'female': case 'nữ': case 'nu': return 'Nữ';
        case '2': case 'other': case 'khác': return 'Khác';
        default: return 'Chưa cập nhật';
    }
}
