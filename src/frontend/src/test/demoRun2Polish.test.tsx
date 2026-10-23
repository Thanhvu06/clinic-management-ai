import type { ReactNode } from 'react';
import { readFileSync } from 'node:fs';
import { cleanup, render, screen, within } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import axiosClient from '../api/axiosClient';
import { CopilotResourceProvider } from '../components/copilot/copilotResourceContext';
import { AdminMedicines } from '../pages/admin/AdminMedicines';
import { AdminDashboard } from '../pages/admin/AdminDashboard';
import medicineStyles from '../pages/admin/AdminMedicines.module.css';
import { getAuditActionLabel, getAuditEntityLabel } from '../utils/auditLogLabels';
import '../theme/browserCompat';

const dialog = vi.hoisted(() => ({ showAlert: vi.fn(), showConfirm: vi.fn(), showToast: vi.fn() }));
vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn(), post: vi.fn(), put: vi.fn(), patch: vi.fn(), delete: vi.fn() } }));
vi.mock('../contexts/DialogContext', () => ({
    useDialog: () => dialog,
    DialogProvider: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

const ok = <T,>(data: T) => ({ success: true, message: '', data });
const get = vi.mocked(axiosClient.get);
const mount = (page: ReactNode) => render(<ConfigProvider theme={{ token: { motion: false } }}><CopilotResourceProvider><MemoryRouter>{page}</MemoryRouter></CopilotResourceProvider></ConfigProvider>);
const longCategory = 'Thuốc hô hấp, long đờm, giảm ho và các thuốc tác dụng trên đường hô hấp';

beforeEach(() => { vi.resetAllMocks(); });
afterEach(() => cleanup());

describe('medicine catalog table polish', () => {
    beforeEach(() => {
        get.mockImplementation(async (url: string) => url.includes('medicine-categories') ? ok([]) : ok({ items: [
            { id: 1, code: 'ACET200', name: 'Acetylcysteine 200mg', unit: 'Gói', stockQuantity: 120, usableStock: 80, reorderLevel: 20,
                isActive: true, isPrescriptionRequired: false, categoryName: longCategory },
        ], totalItems: 1 }) as never);
    });

    it('uses short stock headers and moves the expired-batch hint under the table', async () => {
        mount(<AdminMedicines />);
        const table = await screen.findByRole('table', { name: 'Danh mục thuốc' });
        expect(within(table).getByRole('columnheader', { name: 'Còn dùng được' })).toBeInTheDocument();
        expect(within(table).getByRole('columnheader', { name: 'Tổng tồn' })).toBeInTheDocument();
        expect(within(table).queryByRole('columnheader', { name: 'Tổng tồn (gồm lô hết hạn)' })).not.toBeInTheDocument();
        expect(screen.getByText('Tổng tồn gồm cả lô đã hết hạn.')).toBeInTheDocument();
    });

    it('keeps medicine names whole, clamps the category with a title and keeps both row actions together', async () => {
        mount(<AdminMedicines />);
        const table = await screen.findByRole('table', { name: 'Danh mục thuốc' });
        expect(medicineStyles.medicineNameCell).toBeTruthy();
        expect(within(table).getByText('Acetylcysteine 200mg').closest('td')).toHaveClass(medicineStyles.medicineNameCell);
        const category = within(table).getByText(longCategory);
        expect(category).toHaveAttribute('title', longCategory);
        expect(category).toHaveClass(medicineStyles.medicineCategory);
        const actions = within(table).getByRole('button', { name: 'Sửa' }).closest('td')!;
        expect(actions).toHaveClass(medicineStyles.medicineActionsCell);
        expect(within(actions).getByRole('button', { name: 'Khóa' })).toBeInTheDocument();
    });

    it('declares the no-mid-word-break, two-line clamp and nowrap action rules', () => {
        const css = readFileSync('src/pages/admin/AdminMedicines.module.css', 'utf8');
        expect(css).toMatch(/\.medicineNameCell \{[^}]*word-break: normal;[^}]*overflow-wrap: normal;/);
        expect(css).toMatch(/\.medicineCategory \{[^}]*-webkit-line-clamp: 2;[^}]*text-overflow: ellipsis;/);
        expect(css).toMatch(/\.medicineActionsCell \{[^}]*white-space: nowrap;/);
    });
});

describe('admin dashboard recent activity', () => {
    it('shows only time, actor and description, with the action label first in bold', async () => {
        get.mockImplementation(async (url: string) => {
            if (url.startsWith('/admin/stats')) return ok({ totalAppointmentsToday: 1, totalPatients: 1, totalDoctors: 1, lowStockMedicinesCount: 0 }) as never;
            if (url.startsWith('/admin/audit-logs')) return ok({ items: [{ id: 3, action: 'DiagnosticResultsVerified', entityName: 'DiagnosticOrder',
                userFullName: 'BS. Lan', description: 'Duyệt kết quả phiếu #DX-1 (Bác sĩ).', createdAt: '2026-10-23T02:00:00Z' }], totalItems: 1 }) as never;
            return ok({ items: [], totalItems: 0 }) as never;
        });
        mount(<AdminDashboard />);
        const table = await screen.findByRole('table', { name: 'Hoạt động gần đây' });
        expect(within(table).getAllByRole('columnheader').map(header => header.textContent)).toEqual(['Thời gian', 'Người làm', 'Mô tả']);
        const row = within(table).getByText('Duyệt kết quả phiếu #DX-1 (Bác sĩ).').closest('tr')!;
        expect(within(row).getAllByRole('cell')).toHaveLength(3);
        const descriptionCell = within(row).getAllByRole('cell')[2];
        const heading = descriptionCell.firstElementChild?.firstElementChild;
        expect(heading?.tagName).toBe('STRONG');
        expect(heading).toHaveTextContent(getAuditActionLabel('DiagnosticResultsVerified'));
        expect(within(descriptionCell).getByText(getAuditEntityLabel('DiagnosticOrder'))).toBeInTheDocument();
    });
});
