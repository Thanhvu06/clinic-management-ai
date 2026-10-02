import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import React from 'react';
import {
    ChatProvider,
    useChatContext,
    validateActionSchema,
    validateBookingDraftSchema,
    validateChatMessageSchema,
    DEFAULT_AI_MESSAGE
} from '../contexts/ChatContext';
import type { AiAction, AiBookingDraft, ChatMessage } from '../types/ai';

let currentMockUser: { id: string; fullName: string; role: string } | null = {
    id: 'user-a',
    fullName: 'User A',
    role: 'Patient'
};

vi.mock('../auth/AuthContext', () => ({
    useAuth: () => ({
        isAuthenticated: !!currentMockUser,
        user: currentMockUser,
        loading: false,
        logout: vi.fn(),
    }),
}));

const TestHarness: React.FC = () => {
    const { messages, activeDraft, addMessage, setActiveDraft, clearChat } = useChatContext();

    return (
        <div>
            <div data-testid="msg-count">{messages.length}</div>
            <div data-testid="first-msg">{messages[0]?.content ?? ''}</div>
            <div data-testid="last-msg">{messages[messages.length - 1]?.content ?? ''}</div>
            <div data-testid="draft-specialty">{activeDraft?.specialtyId ?? ''}</div>
            <div data-testid="draft-reason">{activeDraft?.reason ?? ''}</div>
            <button
                data-testid="add-msg-btn"
                onClick={() => addMessage({ role: 'user', content: 'Message from ' + (currentMockUser?.id ?? 'anonymous') })}
            >
                Add Msg
            </button>
            <button
                data-testid="set-draft-btn"
                onClick={() => setActiveDraft({
                    isComplete: false,
                    specialtyId: 101,
                    reason: 'Triệu chứng đau ngực của ' + (currentMockUser?.id ?? 'anonymous')
                })}
            >
                Set Draft
            </button>
            <button data-testid="clear-chat-btn" onClick={clearChat}>
                Clear
            </button>
        </div>
    );
};

describe('ChatContext - Zero-Frame User Isolation & Schema Validation', () => {
    beforeEach(() => {
        vi.clearAllMocks();
        sessionStorage.clear();
        currentMockUser = { id: 'user-a', fullName: 'User A', role: 'Patient' };
    });

    describe('Schema Validators', () => {
        it('validateActionSchema accepts valid actions and rejects invalid payloads or routes', () => {
            const validAction: AiAction = {
                id: 'act-1',
                type: 'ViewSpecialty',
                label: 'Xem chuyên khoa',
                style: 'primary',
                requiresAuthentication: false,
                requiresConfirmation: false,
                payload: { specialtyId: 1, targetUrl: '/patient/book' }
            };
            expect(validateActionSchema(validAction)).toBe(true);

            // Rejects invalid date
            const badDateAction = {
                ...validAction,
                payload: { ...validAction.payload, slotDate: '2026/09/10' }
            };
            expect(validateActionSchema(badDateAction)).toBe(false);

            // Rejects invalid time
            const badTimeAction = {
                ...validAction,
                payload: { ...validAction.payload, startTime: '8:00' }
            };
            expect(validateActionSchema(badTimeAction)).toBe(false);

            // Rejects dangerous targetUrl
            const badUrlAction = {
                ...validAction,
                payload: { ...validAction.payload, targetUrl: 'https://external-phishing.com' }
            };
            expect(validateActionSchema(badUrlAction)).toBe(false);

            // Accepts emergency tel:115
            const emergencyAction: AiAction = {
                id: 'act-em',
                type: 'CallEmergency',
                label: 'Cấp cứu',
                style: 'danger',
                requiresAuthentication: false,
                requiresConfirmation: false,
                payload: { targetUrl: 'tel:115' }
            };
            expect(validateActionSchema(emergencyAction)).toBe(true);
        });

        it('validateBookingDraftSchema validates field types and reason limits', () => {
            const validDraft: AiBookingDraft = {
                isComplete: false,
                specialtyId: 1,
                doctorId: 2,
                slotId: 3,
                slotDate: '2026-09-10',
                startTime: '08:30:00',
                endTime: '09:00:00',
                reason: 'Khám kiểm tra sức khỏe'
            };
            expect(validateBookingDraftSchema(validDraft)).toBe(true);

            // Rejects reason > 500 chars
            const tooLongDraft: AiBookingDraft = {
                ...validDraft,
                reason: 'A'.repeat(501)
            };
            expect(validateBookingDraftSchema(tooLongDraft)).toBe(false);

            // Rejects non-boolean isComplete
            expect(validateBookingDraftSchema({ isComplete: 'true' })).toBe(false);
        });

        it('validateChatMessageSchema validates message roles and nested schemas', () => {
            const validMsg: ChatMessage = {
                role: 'model',
                content: 'Xin chào',
                urgency: 'ROUTINE'
            };
            expect(validateChatMessageSchema(validMsg)).toBe(true);

            // Rejects invalid role
            expect(validateChatMessageSchema({ role: 'admin', content: 'test' })).toBe(false);

            // Rejects message with invalid nested action
            const msgWithBadAction = {
                ...validMsg,
                actions: [{ id: 123 }] // bad action schema
            };
            expect(validateChatMessageSchema(msgWithBadAction)).toBe(false);
        });
    });

    describe('Zero-Frame User Isolation', () => {
        it('prevents any state leakage when switching between User A and User B', async () => {
            // Step 1: Render as User A
            currentMockUser = { id: 'user-a', fullName: 'User A', role: 'Patient' };
            const { unmount, rerender } = render(
                <ChatProvider>
                    <TestHarness />
                </ChatProvider>
            );

            expect(screen.getByTestId('first-msg').textContent).toBe(DEFAULT_AI_MESSAGE.content);
            expect(screen.getByTestId('draft-reason').textContent).toBe('');

            // User A adds a message and sets draft
            fireEvent.click(screen.getByTestId('add-msg-btn'));
            fireEvent.click(screen.getByTestId('set-draft-btn'));

            await waitFor(() => {
                expect(screen.getByTestId('last-msg').textContent).toBe('Message from user-a');
                expect(screen.getByTestId('draft-reason').textContent).toBe('Triệu chứng đau ngực của user-a');
            });

            // Verify User A's session storage
            expect(sessionStorage.getItem('cliniccare_chat_history_user-a')).toBeTruthy();
            expect(sessionStorage.getItem('cliniccare_booking_draft_user-a')).toBeTruthy();

            // Step 2: Switch immediately to User B (simulating auth switch)
            currentMockUser = { id: 'user-b', fullName: 'User B', role: 'Patient' };
            rerender(
                <ChatProvider>
                    <TestHarness />
                </ChatProvider>
            );

            // Zero-frame leakage assertion: User B sees ONLY their clean default state, 0 frames of User A's data
            expect(screen.getByTestId('last-msg').textContent).toBe(DEFAULT_AI_MESSAGE.content);
            expect(screen.getByTestId('draft-reason').textContent).toBe('');
            expect(screen.getByTestId('draft-specialty').textContent).toBe('');

            // User B adds their own message and draft
            fireEvent.click(screen.getByTestId('add-msg-btn'));
            fireEvent.click(screen.getByTestId('set-draft-btn'));

            await waitFor(() => {
                expect(screen.getByTestId('last-msg').textContent).toBe('Message from user-b');
                expect(screen.getByTestId('draft-reason').textContent).toBe('Triệu chứng đau ngực của user-b');
            });

            // Verify User B's session storage is separate from User A's
            expect(sessionStorage.getItem('cliniccare_chat_history_user-b')).toBeTruthy();
            expect(sessionStorage.getItem('cliniccare_booking_draft_user-b')).toBeTruthy();

            // Step 3: Switch back to User A
            currentMockUser = { id: 'user-a', fullName: 'User A', role: 'Patient' };
            rerender(
                <ChatProvider>
                    <TestHarness />
                </ChatProvider>
            );

            // User A's state is restored from User A's session storage!
            await waitFor(() => {
                expect(screen.getByTestId('last-msg').textContent).toBe('Message from user-a');
                expect(screen.getByTestId('draft-reason').textContent).toBe('Triệu chứng đau ngực của user-a');
            });

            // Step 4: Logout (user = null)
            currentMockUser = null;
            rerender(
                <ChatProvider>
                    <TestHarness />
                </ChatProvider>
            );

            // Loaded-user guard: All data cleared when unauthenticated
            await waitFor(() => {
                expect(screen.getByTestId('msg-count').textContent).toBe('0');
                expect(screen.getByTestId('draft-reason').textContent).toBe('');
            });

            unmount();
        });

        it('resiliently falls back to default state when sessionStorage has malformed data', async () => {
            // Seed corrupted JSON in storage
            sessionStorage.setItem('cliniccare_chat_history_user-a', '{"not": "valid array}');
            sessionStorage.setItem('cliniccare_booking_draft_user-a', '{"isComplete": "not-a-bool"}');

            currentMockUser = { id: 'user-a', fullName: 'User A', role: 'Patient' };
            render(
                <ChatProvider>
                    <TestHarness />
                </ChatProvider>
            );

            await waitFor(() => {
                expect(screen.getByTestId('first-msg').textContent).toBe(DEFAULT_AI_MESSAGE.content);
                expect(screen.getByTestId('draft-reason').textContent).toBe('');
            });
        });
    });
});
