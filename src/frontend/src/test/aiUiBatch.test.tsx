import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { SafeMarkdown } from '../components/SafeMarkdown';
import { renderCopilotCardData } from '../components/copilot/copilotDataRenderers';
import { isPatientReadAlias } from '../components/copilot/patientPresentation';
import { SuggestionChips } from '../components/copilot/SuggestionChips';

afterEach(() => { cleanup(); vi.restoreAllMocks(); });

describe('patient chat routing', () => {
    it.each(['lịch hẹn của tôi', 'lượt khám của tôi', 'đơn thuốc của tôi', 'kết quả xét nghiệm của tôi', 'hóa đơn của tôi'])(
        'routes %s to the existing deterministic read endpoint', phrase => expect(isPatientReadAlias(phrase)).toBe(true),
    );
});

describe('date-only visit display', () => {
    it('retains the time for a real timestamp', () => {
        const timestamp = '2026-09-28T08:30:00+07:00';
        render(<>{renderCopilotCardData({ type: 'patient_visits', title: 'Lượt khám của tôi', data: [{ visitCode: 'V-2', visitDate: timestamp }] })}</>);
        expect(screen.getByText('Ngày khám:').parentElement).toHaveTextContent(new Date(timestamp).toLocaleString('vi-VN'));
    });
    it('renders DateOnly without a time or timezone conversion', () => {
        render(<>{renderCopilotCardData({ type: 'patient_visits', title: 'Lượt khám của tôi', data: [{ visitCode: 'V-1', visitDate: '2026-09-28' }] })}</>);
        expect(screen.getByText('Ngày khám:').parentElement).toHaveTextContent('Ngày khám: 28/09/2026');
        expect(screen.getByText('Ngày khám:').parentElement).not.toHaveTextContent('00:00');
    });
});

describe('safe markdown links', () => {
    it.each(['//evil.com', 'javascript:alert(1)'])('does not link %s', url => {
        render(<SafeMarkdown content={`[Link](${url})`} />);
        expect(screen.queryByRole('link')).not.toBeInTheDocument();
    });
    it.each(['/patient/appointments', 'https://example.com', 'http://example.com', 'tel:115', 'mailto:clinic@example.com'])(
        'preserves %s', url => {
            render(<SafeMarkdown content={`[Link](${url})`} />);
            expect(screen.getByRole('link')).toHaveAttribute('href', url);
        },
    );
});

function horizontalStrip(onSelect = vi.fn()) {
    vi.spyOn(HTMLElement.prototype, 'scrollHeight', 'get').mockReturnValue(120);
    render(<SuggestionChips suggestions={[
        { code: 'patient.my_visits', label: 'Lượt khám của tôi' },
        { code: 'patient.my_bills', label: 'Hóa đơn của tôi' },
    ]} onSelect={onSelect} />);
    const strip = screen.getByRole('group');
    Object.defineProperties(strip, { scrollWidth: { value: 600 }, clientWidth: { value: 200 } });
    return strip;
}

describe('horizontal suggestion strip mouse controls', () => {
    it('converts the wheel only while more content exists in that direction', () => {
        const strip = horizontalStrip();
        expect(strip).toHaveAttribute('data-horizontal', 'true');
        expect(fireEvent.wheel(strip, { deltaY: 70 })).toBe(false);
        expect(strip.scrollLeft).toBe(70);
        strip.scrollLeft = 400;
        expect(fireEvent.wheel(strip, { deltaY: 70 })).toBe(true);
        expect(fireEvent.wheel(strip, { deltaY: -70 })).toBe(false);
        expect(strip.scrollLeft).toBe(330);
        strip.scrollLeft = 0;
        expect(fireEvent.wheel(strip, { deltaY: -70 })).toBe(true);
    });

    it('scrolls a mouse drag and suppresses its click, but permits a subsequent click', () => {
        const select = vi.fn();
        const strip = horizontalStrip(select);
        const chip = screen.getByRole('button', { name: 'Gợi ý: Lượt khám của tôi' });
        const pointer = (type: string, clientX: number, pointerType = 'mouse') => {
            const event = new Event(type, { bubbles: true, cancelable: true });
            Object.assign(event, { pointerId: 1, pointerType, clientX, button: 0 });
            fireEvent(strip, event);
        };
        pointer('pointerdown', 100);
        pointer('pointermove', 60);
        pointer('pointerup', 60);
        fireEvent.click(chip);
        expect(strip.scrollLeft).toBe(40);
        expect(select).not.toHaveBeenCalled();
        fireEvent.click(chip);
        expect(select).toHaveBeenCalledTimes(1);
    });

    it('allows clicks below the drag threshold and ignores touch pointers', () => {
        const select = vi.fn();
        const strip = horizontalStrip(select);
        for (const [pointerType, end] of [['mouse', 97], ['touch', 30]] as const) {
            for (const [type, clientX] of [['pointerdown', 100], ['pointermove', end], ['pointerup', end]] as const) {
                const event = new Event(type, { bubbles: true, cancelable: true });
                Object.assign(event, { pointerId: 1, pointerType, clientX, button: 0 });
                fireEvent(strip, event);
            }
            fireEvent.click(screen.getByRole('button', { name: 'Gợi ý: Lượt khám của tôi' }));
        }
        expect(strip.scrollLeft).toBe(0);
        expect(select).toHaveBeenCalledTimes(2);
    });
});
