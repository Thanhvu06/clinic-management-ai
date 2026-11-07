import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, expect, it, vi } from 'vitest';
import { MedicalChatWidget } from '../components/MedicalChatWidget';
import { MedicalChatWidgetSources } from '../components/MedicalChatWidgetSources';
import type { ChatMessage } from '../types/ai';

const state = vi.hoisted(() => ({ messages: [] as ChatMessage[] }));
vi.mock('../auth/AuthContext', () => ({ useAuth: () => ({ isAuthenticated: true, user: { role: 'Patient', userId: 'rag-sources' } }) }));
vi.mock('../hooks/useAiBookingFlow', () => ({ useAiBookingFlow: () => ({
    input: '', setInput: vi.fn(), loading: false, submittingBooking: false, errorMsg: '', messages: state.messages,
    activeDraft: null, clearChat: vi.fn(), handleSendMessage: vi.fn(), handleSuggestion: vi.fn(), wizard: null,
    handleWizardStep: vi.fn(), handleActionClick: vi.fn(), confirmToolAction: vi.fn(), cancelToolAction: vi.fn(),
    formatVietnameseDate: (value: string) => value, retryLastRequest: vi.fn(), canRetry: false
}) }));
vi.mock('../components/copilot/useSuggestionMenu', async importOriginal => ({ ...await importOriginal<object>(), useSuggestionMenu: () => [] }));

beforeEach(() => {
    state.messages = [];
    localStorage.clear();
    sessionStorage.clear();
    HTMLElement.prototype.scrollIntoView = vi.fn();
});

const source = { docId: 'prepare-tests', title: 'Chuẩn bị xét nghiệm', heading: 'Nhịn ăn' };
const mount = () => {
    render(<MemoryRouter><MedicalChatWidget /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { name: 'Mở Trợ lý ClinicCare AI' }));
};

it('shows citations below the bot answer using the existing response transport', () => {
    state.messages = [{ role: 'model', content: 'Hỏi nơi lấy mẫu về việc nhịn ăn.',
        toolResults: [{ status: 'completed', resultType: 'knowledge_sources', data: { sources: [source] } }] }];
    mount();
    expect(screen.getByText('Nguồn: Chuẩn bị xét nghiệm – Nhịn ăn')).toBeInTheDocument();
});

it('does not show a citation line when the bot has no sources', () => {
    state.messages = [{ role: 'model', content: 'Bạn cần hỗ trợ gì?' }];
    mount();
    expect(screen.queryByText(/^Nguồn:/)).not.toBeInTheDocument();
});

it('accepts the typed sources field and keeps citations out of user bubbles', () => {
    const { rerender } = render(<MedicalChatWidgetSources message={{ role: 'model', content: 'Hướng dẫn', sources: [source] }} />);
    expect(screen.getByText('Nguồn: Chuẩn bị xét nghiệm – Nhịn ăn')).toBeInTheDocument();
    rerender(<MedicalChatWidgetSources message={{ role: 'user', content: 'Câu hỏi', sources: [source] }} />);
    expect(screen.queryByText(/^Nguồn:/)).not.toBeInTheDocument();
});
