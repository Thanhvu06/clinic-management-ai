import {
    Bell, Calendar, CreditCard, FileText, FlaskConical, PackageCheck,
    Pill, Receipt, RefreshCw, UserX,
} from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { NotificationType } from '../types/notification';

const definitions = {
    [NotificationType.System]: { label: 'Hệ thống', icon: Bell, tone: 'toneSystem' },
    [NotificationType.Appointment]: { label: 'Lịch khám', icon: Calendar, tone: 'toneAppointment' },
    [NotificationType.AppointmentChangeRequest]: { label: 'Thay đổi lịch', icon: RefreshCw, tone: 'toneAppointmentChangeRequest' },
    [NotificationType.Revisit]: { label: 'Tái khám', icon: FileText, tone: 'toneRevisit' },
    [NotificationType.LeaveRequest]: { label: 'Nghỉ phép', icon: UserX, tone: 'toneLeaveRequest' },
    [NotificationType.Prescription]: { label: 'Đơn thuốc / Khám', icon: Pill, tone: 'tonePrescription' },
    [NotificationType.HealthPackage]: { label: 'Gói khám', icon: PackageCheck, tone: 'toneHealthPackage' },
    [NotificationType.Invoice]: { label: 'Hóa đơn', icon: Receipt, tone: 'toneInvoice' },
    [NotificationType.Payment]: { label: 'Thanh toán', icon: CreditCard, tone: 'tonePayment' },
    [NotificationType.Diagnostic]: { label: 'Cận lâm sàng', icon: FlaskConical, tone: 'toneDiagnostic' },
} as const satisfies Record<NotificationType, { label: string; icon: LucideIcon; tone: string }>;

const definition = (type: NotificationType) => definitions[type] ?? definitions[NotificationType.System];

export const notificationTypeLabel = (type: NotificationType) => definition(type).label;
export const notificationTypeIcon = (type: NotificationType): LucideIcon => definition(type).icon;
export const notificationTypeTone = (type: NotificationType) => definition(type).tone;
