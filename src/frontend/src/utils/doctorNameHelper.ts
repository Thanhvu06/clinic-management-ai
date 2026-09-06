/**
 * Helper to cleanly format a doctor's academic title and full name.
 * Prevents duplicated prefixes like "BS. BS.CKI" and stray double dots like "BS..".
 *
 * Examples:
 * - formatDoctorName("BS.CKI", "Nguyễn Minh Khải") -> "BS.CKI Nguyễn Minh Khải"
 * - formatDoctorName("BS", "Nguyễn Minh Khải") -> "BS. Nguyễn Minh Khải"
 * - formatDoctorName("BS.CKII", "BS. Lê Hoàng Nam") -> "BS.CKII Lê Hoàng Nam"
 * - formatDoctorName("ThS.BS", "Phạm Văn Hùng") -> "ThS.BS Phạm Văn Hùng"
 * - formatDoctorName(undefined, "Nguyễn Minh Khải") -> "Nguyễn Minh Khải"
 */
export function formatDoctorName(academicTitle?: string | null, fullName?: string | null): string {
    if (!fullName) return '';
    let cleanName = fullName.trim();
    if (!academicTitle || !academicTitle.trim()) return cleanName;

    const cleanTitle = academicTitle.trim();

    // Strip duplicated title prefixes from name if already present at start of name
    const titleTokens = [
        'BS.CKII', 'BS.CKI', 'BS. CKI', 'BS. CKII',
        'ThS.BS', 'ThS. BS', 'TS.BS', 'TS. BS',
        'PGS.TS', 'GS.TS', 'BS', 'ThS', 'TS', 'PGS', 'GS'
    ];
    for (const token of titleTokens) {
        const regex = new RegExp(`^${token.replace('.', '\\.')}\\.?\\s*`, 'i');
        cleanName = cleanName.replace(regex, '');
    }

    // Ensure cleanTitle does not end with multiple periods
    let formattedTitle = cleanTitle.replace(/\.+$/, '');

    // Standard Vietnamese abbreviation rule: single word abbreviation like BS, ThS, TS should end with a dot
    if (/^(BS|ThS|TS|PGS|GS)$/i.test(formattedTitle)) {
        formattedTitle += '.';
    }

    return `${formattedTitle} ${cleanName}`.trim();
}

/**
 * Calculates the next working day (Monday - Saturday) in YYYY-MM-DD string format.
 * If base date or base date + 1 day is a Sunday, rolls forward to Monday.
 */
export function getNextWorkingDateString(baseDate: Date = new Date()): string {
    const target = new Date(baseDate);
    // Start by advancing 1 day
    target.setDate(target.getDate() + 1);

    // If target is Sunday (0), roll forward to Monday (+1 day)
    if (target.getDay() === 0) {
        target.setDate(target.getDate() + 1);
    }

    const year = target.getFullYear();
    const month = String(target.getMonth() + 1).padStart(2, '0');
    const day = String(target.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
}

/**
 * Checks if a given YYYY-MM-DD date string represents a Sunday.
 */
export function isSundayDateString(dateStr: string): boolean {
    if (!dateStr) return false;
    const parts = dateStr.split('-');
    if (parts.length !== 3) return false;
    const year = parseInt(parts[0], 10);
    const month = parseInt(parts[1], 10) - 1;
    const day = parseInt(parts[2], 10);
    const d = new Date(year, month, day);
    return d.getDay() === 0;
}
