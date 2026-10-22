import { act, cleanup, fireEvent, render, renderHook, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { readdirSync, readFileSync } from 'node:fs';
import { CheckInTicketModal } from '../components/CheckInTicketModal';
import { InvoiceReceiptModal } from '../components/billing/InvoiceReceiptModal';
import { useScopedPrint } from '../hooks/useScopedPrint';
import type { CheckInTicketDto, InvoiceDetailDto } from '../types';

const ticket: CheckInTicketDto = {
    visitId: 1, visitCode: 'VIS-1', patientId: 1, patientName: 'Người bệnh', medicalRecordNumber: 'MRN-1',
    phoneNumber: '0901234567', queueNumber: 2, queueDisplay: '002', facilityId: 1, facilityName: 'Phòng khám',
    departmentId: 1, departmentName: 'Nội tổng quát', checkedInAtUtc: '2026-10-22T01:00:00Z',
    receptionistName: 'Lễ tân', status: 'WaitingForDoctor', priority: 'Normal', arrivalType: 'Scheduled'
};
const invoice: InvoiceDetailDto = {
    id: 1, invoiceCode: 'INV-1', patientId: 1, patientName: 'Người bệnh', patientPhone: '0901234567',
    sourceType: 1, sourceTypeName: 'Lượt khám', status: 1, statusName: 'Chờ thanh toán',
    subtotal: 100000, totalAmount: 100000, createdAtUtc: '2026-10-22T01:00:00Z', items: [], payments: []
};

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('Scoped modal printing', () => {
    it.each(['receipt', 'ticket'] as const)('scopes %s until afterprint and cleans up on close', kind => {
        const bodyClass = kind === 'receipt' ? 'cc-receipt-printing' : 'cc-ticket-printing';
        const component = (open: boolean) => kind === 'receipt'
            ? <InvoiceReceiptModal isOpen={open} invoice={invoice} onClose={vi.fn()} />
            : <CheckInTicketModal isOpen={open} ticket={ticket} onClose={vi.fn()} />;
        const print = vi.spyOn(window, 'print').mockImplementation(() => {
            expect(document.body).toHaveClass(bodyClass);
        });
        const view = render(component(true));
        const button = screen.getByRole('button', { name: /In / });
        expect(document.body).not.toHaveClass(bodyClass);
        fireEvent.click(button);
        expect(print).toHaveBeenCalledOnce();
        expect(document.body).toHaveClass(bodyClass);
        fireEvent(window, new Event('afterprint'));
        expect(document.body).not.toHaveClass(bodyClass);
        fireEvent.click(button);
        view.rerender(component(false));
        expect(document.body).not.toHaveClass(bodyClass);
    });

    it.each(['cc-receipt-printing', 'cc-ticket-printing'])('cleans up %s when print throws or the modal unmounts', bodyClass => {
        const print = vi.spyOn(window, 'print').mockImplementation(() => { throw new Error('Print unavailable'); });
        const hook = renderHook(() => useScopedPrint(bodyClass, true));
        expect(() => act(() => hook.result.current())).toThrow('Print unavailable');
        expect(document.body).not.toHaveClass(bodyClass);
        print.mockImplementation(() => {});
        act(() => hook.result.current());
        expect(document.body).toHaveClass(bodyClass);
        hook.unmount();
        expect(document.body).not.toHaveClass(bodyClass);
    });

    it('requires a class scope on every universal selector in print styles', () => {
        const paths = readdirSync('src', { recursive: true }).filter(path => String(path).endsWith('.css'));
        for (const path of paths) {
            const css = readFileSync(`src/${path}`, 'utf8');
            for (const match of css.matchAll(/@media\s+print\s*\{/g)) {
                let depth = 1;
                let end = match.index! + match[0].length;
                for (; end < css.length && depth > 0; end++) {
                    if (css[end] === '{') depth++;
                    if (css[end] === '}') depth--;
                }
                const printCss = css.slice(match.index! + match[0].length, end - 1);
                for (const rule of printCss.matchAll(/([^{}]+)\{/g)) {
                    for (const selector of rule[1].split(',')) {
                        if (selector.includes('*')) expect(selector, `${path}: ${selector}`).toMatch(/\.[\w-]+/);
                    }
                }
            }
        }
    });
});
