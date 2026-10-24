import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Clock } from 'lucide-react';
import { MetricStrip, StatCard } from '../components/common';
import styles from '../components/common/MetricStrip.module.css';
import { formatDateTime } from '../utils/formatters';

afterEach(cleanup);

const value = (label: string) => screen.getByText(label).nextElementSibling as HTMLElement;

describe('MetricStrip', () => {
    it('renders each label and value as one cell without icons', () => {
        const { container } = render(<MemoryRouter><MetricStrip ariaLabel="Số liệu" items={[
            { label: 'Đơn chờ cấp', value: 3, hint: 'Cần xử lý' },
            { label: 'Đã cấp hôm nay', value: 12 },
        ]} /></MemoryRouter>);
        const strip = screen.getByRole('group', { name: 'Số liệu' });
        expect(strip.children).toHaveLength(2);
        expect(value('Đơn chờ cấp')).toHaveTextContent('3');
        expect(value('Đã cấp hôm nay')).toHaveTextContent('12');
        expect(screen.getByText('Cần xử lý')).toHaveClass(styles.hint);
        expect(value('Đơn chờ cấp')).toHaveClass(styles.value);
        expect(container.querySelector('svg')).toBeNull();
    });

    it('marks attention only when the value is greater than zero', () => {
        render(<MemoryRouter><MetricStrip items={[
            { label: 'Có việc', value: 2, tone: 'attention' },
            { label: 'Không việc', value: 0, tone: 'attention' },
            { label: 'Đang tải', value: '...', tone: 'attention' },
            { label: 'Mặc định', value: 5 },
        ]} /></MemoryRouter>);
        expect(value('Có việc')).toHaveAttribute('data-tone', 'attention');
        expect(value('Có việc')).toHaveClass(styles.valueAttention);
        for (const label of ['Không việc', 'Đang tải', 'Mặc định']) {
            expect(value(label)).toHaveAttribute('data-tone', 'default');
            expect(value(label)).not.toHaveClass(styles.valueAttention);
        }
    });

    it('turns the whole cell into a link when to is set', () => {
        render(<MemoryRouter><MetricStrip items={[
            { label: 'Thuốc dưới định mức', value: 4, to: '/pharmacy/inventory' },
            { label: 'Đã cấp hôm nay', value: 1 },
        ]} /></MemoryRouter>);
        const link = screen.getByRole('link', { name: /Thuốc dưới định mức/ });
        expect(link).toHaveAttribute('href', '/pharmacy/inventory');
        expect(within(link).getByText('4')).toBeInTheDocument();
        expect(screen.getAllByRole('link')).toHaveLength(1);
    });

    it('renders a pressed filter button when onClick is set', () => {
        const onClick = vi.fn();
        render(<MemoryRouter><MetricStrip items={[{ label: 'Đang thực hiện', value: 2, onClick, pressed: true, ariaLabel: 'Lọc: Đang thực hiện' }]} /></MemoryRouter>);
        const button = screen.getByRole('button', { name: 'Lọc: Đang thực hiện' });
        expect(button).toHaveAttribute('aria-pressed', 'true');
        fireEvent.click(button);
        expect(onClick).toHaveBeenCalledTimes(1);
    });
});

describe('StatCard', () => {
    it('keeps its props but renders the MetricStrip cell without the icon', () => {
        const { container } = render(<StatCard title="Doanh thu" value={10} subtitle="Hôm nay" icon={<Clock />} color="warning" />);
        const card = screen.getByText('Doanh thu').closest('.ant-card');
        expect(card).toHaveClass(styles.standalone);
        expect(container.querySelector('svg')).toBeNull();
        expect(screen.getByText('10')).toHaveClass(styles.value);
        expect(screen.getByText('10')).toHaveAttribute('data-tone', 'attention');
        expect(screen.getByText('Hôm nay')).toBeInTheDocument();
    });
});

describe('formatDateTime', () => {
    it('formats timestamps as dd/MM/yyyy HH:mm', () => {
        expect(formatDateTime(new Date(2026, 9, 5, 7, 3))).toBe('05/10/2026 07:03');
        expect(formatDateTime('')).toBe('—');
        expect(formatDateTime('không hợp lệ')).toBe('không hợp lệ');
    });
});
