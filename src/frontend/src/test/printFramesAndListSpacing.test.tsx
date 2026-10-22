import { cleanup, render, screen } from '@testing-library/react';
import { readFileSync } from 'node:fs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { InvoiceReceiptModal } from '../components/billing/InvoiceReceiptModal';
import type { InvoiceDetailDto } from '../types';

afterEach(cleanup);

const screenOnlyBlock = (css: string) => {
    const start = css.indexOf('@media screen {');
    return start < 0 ? '' : css.slice(start, css.indexOf('@media', start + 1));
};

describe('U7 print frames on small screens', () => {
    it.each([
        'src/pages/doctor/DiagnosticOrderPrint.module.css',
        'src/pages/common/DiagnosticResultPrint.module.css',
        'src/components/billing/InvoiceReceiptModal.module.css',
    ])('%s scrolls its table only on screen and caps the frame on narrow screens', path => {
        const css = readFileSync(path, 'utf8');
        expect(screenOnlyBlock(css)).toMatch(/\.tableScroll\s*\{[^}]*overflow-x: auto/);
        expect(css).toMatch(/@media screen and \(max-width: 767px\)/);
        expect(css.slice(0, css.indexOf('@media screen {'))).not.toMatch(/\.tableScroll/);
        const print = css.indexOf('@media print');
        if (print >= 0) expect(css.slice(print)).not.toMatch(/\.tableScroll/);
    });

    it.each([
        ['src/pages/doctor/DiagnosticOrderPrint.tsx', 'styles.tableLayout1'],
        ['src/pages/common/DiagnosticResultPrint.tsx', 'styles.values'],
    ])('%s wraps its table in the screen-only scroll area', (path, tableClass) => {
        const source = readFileSync(path, 'utf8');
        expect(source).toMatch(new RegExp(`className=\\{styles\\.tableScroll\\}>\\s*<table[^>]*className=\\{${tableClass.replace('.', '\\.')}\\}`));
    });

    it('wraps the receipt items table in the scroll area', () => {
        const invoice: InvoiceDetailDto = {
            id: 1, invoiceCode: 'INV-1', patientId: 1, patientName: 'Người bệnh', patientPhone: '0901234567', sourceType: 1, sourceTypeName: 'Lượt khám',
            status: 1, statusName: 'Chờ thanh toán', subtotal: 100000, totalAmount: 100000, createdAtUtc: '2026-10-22T01:00:00Z', items: [], payments: [],
        };
        render(<InvoiceReceiptModal isOpen invoice={invoice} onClose={vi.fn()} />);
        expect(screen.getByRole('table').parentElement!.className).toMatch(/tableScroll/);
    });
});

describe('U9 reception list spacing', () => {
    it.each([
        'src/pages/reception/ReceptionPackageRegistrations.module.css',
        'src/pages/reception/ReceptionChangeRequests.module.css',
    ])('%s does not stack the page gap on top of the header margin', path => {
        expect(readFileSync(path, 'utf8')).toMatch(/\.page > :first-child \{ margin-bottom: 0; \}/);
    });
});
