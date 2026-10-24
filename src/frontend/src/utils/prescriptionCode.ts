export function formatPrescriptionCode(prescription: { id: number; createdAt?: string | null; code?: string | null; prescriptionCode?: string | null }): string {
    const code = prescription.code?.trim() || prescription.prescriptionCode?.trim();
    if (code) return code;
    const date = /^(\d{4})-(\d{2})-(\d{2})/.exec(prescription.createdAt ?? '');
    const stamp = date && Number(date[1]) > 1900 ? `${date[1].slice(2)}${date[2]}${date[3]}` : '000000';
    return `RX-${stamp}-${String(prescription.id).padStart(4, '0')}`;
}

export function formatPrescriptionReference(prescription: Parameters<typeof formatPrescriptionCode>[0], reason?: string): string {
    const code = formatPrescriptionCode(prescription);
    return reason?.replace(new RegExp(`#${prescription.id}(?!\\d)`, 'g'), code) || `Đơn thuốc ${code}`;
}
