import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { Link, MemoryRouter } from 'react-router-dom';
import { afterEach, expect, it, vi } from 'vitest';
import { useOpenPrescriptionFromQuery } from '../pages/pharmacy/useOpenPrescriptionFromQuery';

afterEach(cleanup);

const Probe = ({ loaded, open }: { loaded: boolean; open: (id: number) => void }) => {
    useOpenPrescriptionFromQuery(loaded, open);
    return <Link to="?id=12">Đơn tiếp theo</Link>;
};

it('waits for the list, opens a query id once, then follows router search changes', async () => {
    const open = vi.fn();
    const view = render(<MemoryRouter initialEntries={['/pharmacy/prescriptions?id=10']}><Probe loaded={false} open={open} /></MemoryRouter>);
    expect(open).not.toHaveBeenCalled();
    view.rerender(<MemoryRouter initialEntries={['/pharmacy/prescriptions?id=10']}><Probe loaded open={open} /></MemoryRouter>);
    await waitFor(() => expect(open).toHaveBeenCalledExactlyOnceWith(10));
    view.rerender(<MemoryRouter><Probe loaded open={open} /></MemoryRouter>);
    expect(open).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByRole('link', { name: 'Đơn tiếp theo' }));
    await waitFor(() => expect(open).toHaveBeenLastCalledWith(12));
    expect(open).toHaveBeenCalledTimes(2);
});

it.each(['', '0', '-1', '1.5', 'abc'])('does not open invalid query id %s', id => {
    const open = vi.fn();
    render(<MemoryRouter initialEntries={[`/pharmacy/prescriptions?id=${id}`]}><Probe loaded open={open} /></MemoryRouter>);
    expect(open).not.toHaveBeenCalled();
});
