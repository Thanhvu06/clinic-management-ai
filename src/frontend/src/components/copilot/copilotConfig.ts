import type { AiCopilotTool } from '../../api/aiCopilotApi';

export type CopilotRole = 'Patient' | 'Receptionist' | 'Doctor' | 'DiagnosticTechnician' | 'Pharmacist' | 'Admin';

export interface CopilotRoleConfig {
    label: string;
    shortLabel: string;
    description: string;
    prompts: string[];
    navigationLabel: string;
    workspacePath: string;
    defaultTools: string[];
}

export const COPILOT_ROLE_CONFIG: Record<CopilotRole, CopilotRoleConfig> = {
    Patient: {
        label: 'Trợ lý bệnh nhân', shortLabel: 'Bệnh nhân',
        description: 'Tra cứu thông tin phòng khám, chuẩn bị đặt lịch và xem dữ liệu của chính bạn.',
        prompts: ['Tôi nên khám chuyên khoa nào?', 'Tìm lịch khám sớm nhất.', 'Xem lịch hẹn của tôi.'],
        navigationLabel: 'Mở khu vực bệnh nhân', workspacePath: '/patient',
        defaultTools: ['clinic.search_knowledge', 'patient.get_my_appointments']
    },
    Receptionist: {
        label: 'Copilot Lễ tân', shortLabel: 'Lễ tân',
        description: 'Hỗ trợ lịch hẹn, hàng đợi và thông tin vận hành trong cơ sở được phân quyền.',
        prompts: ['Xem lịch hẹn hôm nay', 'Xem hàng đợi tiếp nhận', 'Tra cứu mã lịch hẹn'],
        navigationLabel: 'Mở bàn tiếp tân', workspacePath: '/reception',
        defaultTools: ['reception.get_today_appointments', 'reception.get_queue', 'reception.lookup_appointment']
    },
    Doctor: {
        label: 'Copilot Bác sĩ', shortLabel: 'Bác sĩ',
        description: 'Tra cứu ca được phân công, bệnh nhân hiện tại và chỉ định cận lâm sàng.',
        prompts: ['Xem hàng đợi của tôi', 'Tóm tắt bệnh nhân hiện tại', 'Xem chỉ định cận lâm sàng'],
        navigationLabel: 'Mở bàn khám', workspacePath: '/doctor',
        defaultTools: ['doctor.get_my_queue', 'doctor.get_patient_summary', 'doctor.get_diagnostic_orders']
    },
    DiagnosticTechnician: {
        label: 'Copilot Kỹ thuật viên', shortLabel: 'Kỹ thuật viên',
        description: 'Tra cứu worklist cận lâm sàng thuộc phạm vi phân công.',
        prompts: ['Xem danh sách chỉ định đang chờ', 'Tra cứu hướng dẫn trước xét nghiệm', 'Xem giờ làm việc phòng xét nghiệm'],
        navigationLabel: 'Mở worklist cận lâm sàng', workspacePath: '/diagnostics',
        defaultTools: ['technician.get_worklist', 'clinic.search_knowledge']
    },
    Pharmacist: {
        label: 'Copilot Dược sĩ', shortLabel: 'Dược sĩ',
        description: 'Tra cứu đơn thuốc, tồn kho và hướng dẫn đã được phòng khám phê duyệt.',
        prompts: ['Xem đơn thuốc chờ cấp', 'Xem tồn kho', 'Tra cứu hướng dẫn sử dụng thuốc'],
        navigationLabel: 'Mở bàn dược', workspacePath: '/pharmacy',
        defaultTools: ['pharmacist.get_prescription_queue', 'pharmacist.get_inventory_status', 'clinic.search_knowledge']
    },
    Admin: {
        label: 'Copilot Quản trị viên', shortLabel: 'Quản trị viên',
        description: 'Xem chỉ số tổng hợp, sức khỏe AI và kiến thức vận hành không chứa hồ sơ lâm sàng.',
        prompts: ['Xem thống kê hôm nay', 'Xem tình trạng AI', 'Tra cứu giờ làm việc phòng khám'],
        navigationLabel: 'Mở tổng quan quản trị', workspacePath: '/admin',
        defaultTools: ['admin.get_dashboard_metrics', 'admin.get_ai_health', 'clinic.search_knowledge']
    }
};

export const getCopilotRoleConfig = (role?: string): CopilotRoleConfig =>
    COPILOT_ROLE_CONFIG[(role as CopilotRole) || 'Patient'] ?? COPILOT_ROLE_CONFIG.Patient;

export const providerStateLabel = (state?: string): string => ({
    NotCalled: 'Xử lý nội bộ',
    Online: 'Gemini đã phản hồi',
    Degraded: 'Đang dùng chế độ dự phòng',
    Unavailable: 'Dịch vụ AI chưa cấu hình/không khả dụng',
    Disabled: 'AI bị tắt cấu hình; đang dùng hỗ trợ cơ bản',
    SafetyBlocked: 'Đã chặn vì an toàn'
}[state ?? 'NotCalled'] ?? 'Trạng thái AI chưa xác định');

export const toolDisplayName = (tool: AiCopilotTool | string): string => {
    const name = typeof tool === 'string' ? tool : tool.name;
    return ({
        'reception.prepare_check_in_appointment': 'Tiếp nhận lịch hẹn',
        'reception.prepare_create_walk_in': 'Tiếp nhận bệnh nhân vãng lai',
        'doctor.prepare_diagnostic_order': 'Chuẩn bị chỉ định cận lâm sàng',
        'doctor.prepare_prescription_draft': 'Chuẩn bị bản nháp đơn thuốc',
        'technician.prepare_start_diagnostic_order': 'Tiếp nhận phiếu chỉ định',
        'technician.prepare_record_diagnostic_result': 'Lưu kết quả kỹ thuật',
        'technician.prepare_complete_diagnostic_order': 'Hoàn tất phiếu chỉ định',
        'pharmacist.prepare_reserve_prescription': 'Giữ chỗ thuốc theo đơn',
        'pharmacist.prepare_dispense_prescription': 'Cấp phát đơn thuốc'
    } as Record<string, string>)[name] ?? 'Thao tác nghiệp vụ';
};
