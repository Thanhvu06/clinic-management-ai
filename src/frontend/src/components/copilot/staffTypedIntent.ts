import { findPhrase, typedTokens } from './typedIntentText';

// Routes a typed staff question to one server-owned suggestion code. Anything
// unclear returns null and keeps the existing free-text path; the server still
// authorizes every code against the signed-in role.

type Has = (phrase: string, fuzzy?: boolean) => boolean;

const OTHER_DAY = ['ngay mai', 'tuan nay', 'tuan toi', 'tuan sau', 'sap toi', 'hom qua', 'thang nay'];

const receptionist = (has: Has, any: (phrases: string[]) => boolean): string | null => {
    if (any(['chua thanh toan', 'cho thanh toan', 'con no'])) return 'receptionist.pending_payments';
    // "lượt hẹn" stays on the free-text path; schedule questions need "lịch" or "cuộc hẹn".
    const schedule = has('lich', false) || has('cuoc hen');
    if (schedule && any(['sap toi', 'tuan nay', 'ngay mai', 'tuan toi'])) return 'receptionist.upcoming_appointments';
    if (schedule && has('hom nay')) return 'receptionist.today_appointments';
    if (any(['hang doi', 'cho tiep nhan', 'dang cho'])) return 'receptionist.queue';
    return null;
};

const doctor = (has: Has, any: (phrases: string[]) => boolean): string | null => {
    if (any(['kham ai', 'hang doi', 'benh nhan cho'])) return 'doctor.my_queue';
    if (any(['lich hen', 'lich kham']) && (has('hom nay') || !any(OTHER_DAY))) return 'doctor.today_appointments';
    return null;
};

const technician = (has: Has, any: (phrases: string[]) => boolean): string | null => {
    if (any(['da xong', 'hoan tat', 'hoan thanh']) && has('hom nay')) return 'technician.completed_today';
    if (any(['chi dinh', 'phieu', 'worklist', 'can lam'])) return 'technician.worklist';
    return null;
};

const pharmacist = (has: Has, any: (phrases: string[]) => boolean): string | null => {
    if (any(['sap het', 'het hang', 'ton thap', 'can nhap'])) return 'pharmacist.low_stock';
    if (any(['don cho', 'don thuoc cho', 'cho cap'])) return 'pharmacist.prescription_queue';
    if (has('ton kho')) return 'pharmacist.inventory';
    return null;
};

const admin = (has: Has, any: (phrases: string[]) => boolean, tokens: readonly string[]): string | null => {
    if (has('doanh thu')) {
        if (has('thang nay')) return 'admin.revenue_this_month';
        // Another explicit period has no fixed code; let the server handle it.
        if (any(['hom qua', 'thang truoc', 'tuan', 'nam nay', 'quy'])) return null;
        return 'admin.revenue_today';
    }
    if ((has('tro ly') || tokens.includes('ai')) && any(['hoat dong', 'suc khoe'])) return 'admin.ai_health';
    if (any(['chi so', 'thong ke', 'tong quan'])) return 'admin.dashboard_metrics';
    return null;
};

/**
 * Returns a suggestion code for a typed staff question, or null. When
 * `menuCodes` is given (the user's current server-issued menu), only a code
 * from that menu is returned.
 */
export function classifyStaffTypedIntent(role: string, text: string, menuCodes?: readonly string[]): string | null {
    if (text.length > 120) return null;
    // Record codes and long numbers go to the server's own lookup.
    if (/\b(?:APT|VIS|REG|INV)-/i.test(text) || /\d{6,}/.test(text)) return null;
    const tokens = typedTokens(text);
    const has: Has = (phrase, fuzzy = true) => findPhrase(tokens, phrase, fuzzy) >= 0;
    const any = (phrases: string[]) => phrases.some(phrase => has(phrase));
    // "How do I…" questions are not data lookups.
    if (has('cach', false) || has('the nao', false) || has('huong dan')) return null;

    const code = role === 'Receptionist' ? receptionist(has, any)
        : role === 'Doctor' ? doctor(has, any)
        : role === 'DiagnosticTechnician' ? technician(has, any)
        : role === 'Pharmacist' ? pharmacist(has, any)
        : role === 'Admin' ? admin(has, any, tokens)
        : null;
    if (!code || (menuCodes && !menuCodes.includes(code))) return null;
    return code;
}
