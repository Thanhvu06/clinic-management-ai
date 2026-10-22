import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { UnifiedCopilotPanel } from '../components/copilot/UnifiedCopilotPanel';
import { COPILOT_ROLE_CONFIG } from '../components/copilot/copilotConfig';

const mocks = vi.hoisted(() => ({
    user: { userId: 'reception-1', fullName: 'Lễ tân Hoa', role: 'Receptionist' },
    catalog: vi.fn(),
}));

vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ user: mocks.user, identityVersion: 1 }) }));
vi.mock('../api/aiCopilotApi', () => ({
    sendRoleCopilotMessage: vi.fn(),
    getRoleCopilotCatalog: (...args: unknown[]) => mocks.catalog(...args),
    prepareRoleAction: vi.fn(),
    confirmRoleAction: vi.fn(),
    cancelRoleAction: vi.fn(),
}));
vi.mock('../api/aiSuggestionApi', () => ({ getCopilotSuggestions: async () => ({ role: 'Receptionist', suggestions: [] }) }));

describe('Compact Copilot launcher', () => {
    beforeEach(() => {
        mocks.catalog.mockReset().mockResolvedValue({ tools: [], actionTools: [] });
    });

    it.each(['Receptionist', 'Doctor', 'Pharmacist', 'DiagnosticTechnician', 'Admin'] as const)('shows only the icon for %s while keeping the accessible name and label text', role => {
        mocks.user = { ...mocks.user, role };
        const { label, shortLabel } = COPILOT_ROLE_CONFIG[role];
        render(<MemoryRouter><UnifiedCopilotPanel /></MemoryRouter>);

        const launcher = screen.getByRole('button', { name: `Mở ${label}` });
        expect(launcher.querySelector('svg')).not.toBeNull();
        const hiddenLabel = within(launcher).getByText(shortLabel);
        expect(hiddenLabel).toHaveClass('visually-hidden');
        const visibleText = Array.from(launcher.childNodes)
            .filter(node => !(node instanceof HTMLElement && node.classList.contains('visually-hidden')))
            .map(node => node.textContent ?? '')
            .join('')
            .trim();
        expect(visibleText).toBe('');
    });

    it('still opens the panel and returns focus to the same launcher after closing', async () => {
        mocks.user = { ...mocks.user, role: 'Receptionist' };
        render(<MemoryRouter><UnifiedCopilotPanel /></MemoryRouter>);

        fireEvent.click(screen.getByRole('button', { name: 'Mở Copilot Lễ tân' }));
        expect(await screen.findByRole('dialog', { name: 'Copilot Lễ tân' })).toBeInTheDocument();
        await act(async () => { fireEvent.click(screen.getByRole('button', { name: 'Đóng Copilot' })); });
        expect(screen.getByRole('button', { name: 'Mở Copilot Lễ tân' })).toHaveFocus();
    });
});
