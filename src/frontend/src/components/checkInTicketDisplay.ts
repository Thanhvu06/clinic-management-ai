import type { CheckInTicketDto } from '../types';

export const getCheckInTicketDisplay = (ticket: Pick<CheckInTicketDto, 'queueNumber' | 'queueDisplay'>) => ({
    number: String(ticket.queueNumber).padStart(3, '0'),
    departmentCode: (ticket.queueDisplay || '').replace(/-?\d+$/, '')
});
