import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { AdminAuditLogs } from '../pages/admin/AdminAuditLogs';
import { AdminDashboard } from '../pages/admin/AdminDashboard';
import axiosClient from '../api/axiosClient';
import { auditActionLabels, auditEntityLabels, getAuditActionLabel, getAuditEntityLabel } from '../utils/auditLogLabels';

vi.mock('../api/axiosClient', () => ({ default: { get: vi.fn() } }));
afterEach(cleanup);

function backendSources(directory: string): string[] {
    return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
        if (['AI', 'Migrations', 'bin', 'obj'].includes(entry.name)) return [];
        const path = join(directory, entry.name);
        return entry.isDirectory() ? backendSources(path) : entry.name.endsWith('.cs') ? [readFileSync(path, 'utf8')] : [];
    });
}

it('maps every emitted backend audit action and entity, including dynamic helper actions', () => {
    const sources = backendSources('../backend/ClinicManagement.Infrastructure').join('\n');
    const blocks = [...sources.matchAll(/new SystemAuditLog\s*\{([\s\S]*?)\n\s*\}/g)].map(match => match[1]);
    const actions = new Set(blocks.flatMap(block => [...block.matchAll(/\bAction\s*=\s*"([^"]+)"/g)].map(match => match[1])));
    for (const match of sources.matchAll(/(?:AddVerificationAudit|SaveSpecimenStepAsync)\(order,\s*(?:item,\s*)?"([^"]+)"/g)) actions.add(match[1]);
    for (const match of sources.matchAll(/const string SeedAction = "([^"]+)"/g)) actions.add(match[1]);
    const entities = new Set(blocks.flatMap(block => [...block.matchAll(/\bEntityName\s*=\s*(?:"([^"]+)"|nameof\(([^)]+)\))/g)].map(match => match[1] ?? match[2])));
    for (const match of sources.matchAll(/const string SeedEntity = "([^"]+)"/g)) entities.add(match[1]);
    expect(actions.size).toBeGreaterThan(20);
    expect(entities.size).toBeGreaterThan(6);
    expect([...actions].filter(action => !Object.hasOwn(auditActionLabels, action))).toEqual([]);
    expect([...entities].filter(entity => !Object.hasOwn(auditEntityLabels, entity))).toEqual([]);
    expect(getAuditActionLabel('FutureAction')).toBe('FutureAction');
    expect(getAuditEntityLabel('FutureEntity')).toBe('FutureEntity');
    expect(getAuditActionLabel('toString')).toBe('toString');
});

it.each([AdminAuditLogs, AdminDashboard])('renders Vietnamese action and entity on $name', async Component => {
    vi.mocked(axiosClient.get).mockImplementation(async (url: string) => {
        if (url.includes('/stats')) return { success: true, data: {} };
        return { success: true, data: { items: url.includes('/audit-logs') ? [{ id: 1, action: 'PROCESS_PAYMENT', entityName: 'Payment',
            userFullName: 'Lễ tân', description: 'Đã thu tiền', createdAt: '2026-10-23T06:35:00Z' }] : [], totalItems: 1 } };
    });
    render(<MemoryRouter><Component /></MemoryRouter>);
    expect(await screen.findByText('Thu tiền')).toBeInTheDocument();
    expect(screen.getByText('Thanh toán')).toBeInTheDocument();
    expect(screen.queryByText('PROCESS_PAYMENT')).not.toBeInTheDocument();
    expect(screen.queryByText('Payment')).not.toBeInTheDocument();
});
