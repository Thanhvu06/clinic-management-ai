import { useLayoutEffect } from 'react';
import { render, screen } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import { BookingWizard } from '../components/BookingWizard';
import type { AiBookingWizardResponse } from '../types/ai';

it('scrolls the card and focuses the new heading before the parent layout observes a changed step', () => {
    const scrollIntoView = vi.fn();
    HTMLElement.prototype.scrollIntoView = scrollIntoView;
    const observations: Array<{ title: string; scrollCalls: number; headingFocused: boolean }> = [];
    const onStep = vi.fn().mockResolvedValue(undefined);
    const state = (step: AiBookingWizardResponse['step'], title: string): AiBookingWizardResponse => ({
        step, title, message: 'Chọn để tiếp tục', textToken: `opaque-${step}`, options: [], canGoBack: false,
        actions: [], suggestions: [], assistantMode: 'Ready', providerWasCalled: false,
    });
    const ObserveLayout = ({ value }: { value: AiBookingWizardResponse }) => {
        useLayoutEffect(() => {
            const heading = screen.getByRole('heading', { name: value.title });
            observations.push({ title: value.title, scrollCalls: scrollIntoView.mock.calls.length,
                headingFocused: document.activeElement === heading });
        }, [value.step, value.title]);
        return <BookingWizard state={value} busy={false} onStep={onStep} />;
    };
    const view = render(<ObserveLayout value={state('specialty', 'Chọn chuyên khoa')} />);

    view.rerender(<ObserveLayout value={state('day', 'Chọn ngày khám')} />);

    expect(observations).toEqual([
        { title: 'Chọn chuyên khoa', scrollCalls: 1, headingFocused: true },
        { title: 'Chọn ngày khám', scrollCalls: 2, headingFocused: true },
    ]);
    expect(scrollIntoView).toHaveBeenNthCalledWith(1, { block: 'start', behavior: 'instant' });
    expect(scrollIntoView).toHaveBeenNthCalledWith(2, { block: 'start', behavior: 'instant' });
});
