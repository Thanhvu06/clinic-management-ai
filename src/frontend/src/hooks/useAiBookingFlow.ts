import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useChatContext } from "../contexts/ChatContext";
import axiosClient from "../api/axiosClient";
import type { ApiResponse } from "../types";
import type {
    ChatMessage,
    AiChatResponse,
    AiAction,
    AiBookingDraft
} from "../types/ai";

export interface AiChatRequestPayload {
    message: string;
    context: Array<{ role: "user" | "model"; content: string }>;
    pendingSpecialtyId?: number;
    pendingDoctorId?: number;
    pendingSlotId?: number;
    pendingSlotDate?: string;
    reason?: string;
}

export interface CreateAppointmentPayload {
    doctorId: number;
    specialtyId: number;
    appointmentSlotId: number;
    reason: string;
}

export interface AppointmentEntityDto {
    id: number;
    appointmentCode: string;
    patientId: number;
    doctorId: number;
    doctorName?: string;
    specialtyId: number;
    specialtyName?: string;
    appointmentSlotId: number;
    appointmentDate: string;
    startTime: string;
    endTime: string;
    reason: string;
    status: string;
}

export const isSafeClientRoute = (url?: string): boolean => {
    if (!url || typeof url !== "string") return false;
    if (url === "tel:115") return true;
    if (!url.startsWith("/") || url.startsWith("//")) return false;
    if (url.includes("://") || url.includes(":") || url.includes("\\") || /\s/.test(url)) return false;

    const [path] = url.split(/[?#]/);
    const allowedPathPrefixes = [
        "/patient/book",
        "/patient/appointments",
        "/patient/diagnostic-results",
        "/patient/prescriptions",
        "/patient/invoices",
        "/doctors"
    ];

    return allowedPathPrefixes.some(prefix => path === prefix || path.startsWith(prefix + "/"));
};

export const formatVietnameseDate = (dateStr?: string): string => {
    if (!dateStr) return "";
    try {
        const parts = dateStr.split("-");
        if (parts.length === 3) {
            return `${parts[2]}/${parts[1]}/${parts[0]}`;
        }
        const d = new Date(dateStr);
        if (!isNaN(d.getTime())) {
            const day = String(d.getDate()).padStart(2, "0");
            const month = String(d.getMonth() + 1).padStart(2, "0");
            const year = d.getFullYear();
            return `${day}/${month}/${year}`;
        }
    } catch {
        // fallback
    }
    return dateStr;
};

interface SendMessageOptions {
    specialtyId?: number;
    pendingSpecialtyId?: number;
    doctorId?: number;
    pendingDoctorId?: number;
    slotId?: number;
    pendingSlotId?: number;
    slotDate?: string;
    pendingSlotDate?: string;
    reason?: string;
}

export const useAiBookingFlow = (onNavigate?: () => void) => {
    const {
        messages,
        setMessages,
        clearChat,
        setPendingSpecialtyId,
        activeDraft,
        setActiveDraft
    } = useChatContext();

    const [input, setInput] = useState("");
    const [loading, setLoading] = useState(false);
    const [submittingBooking, setSubmittingBooking] = useState(false);
    const [errorMsg, setErrorMsg] = useState("");
    const navigate = useNavigate();

    const handleSendMessage = async (
        textToSend: string,
        pendingPayload?: SendMessageOptions,
        prefixMessage?: ChatMessage
    ) => {
        const trimmed = textToSend.trim();
        if (!trimmed || loading) return;
        if (trimmed.length > 500) {
            setErrorMsg("Tin nhắn quá dài (tối đa 500 ký tự).");
            return;
        }

        const userMsg: ChatMessage = { role: "user", content: trimmed };
        const newMessages = prefixMessage
            ? [...messages, prefixMessage, userMsg]
            : [...messages, userMsg];

        const historyMessages: Array<{ role: "user" | "model"; content: string }> = newMessages
            .slice(-9, -1)
            .map(m => ({ role: m.role, content: m.content }));

        setMessages(newMessages);
        setInput("");
        setErrorMsg("");
        setLoading(true);

        try {
            const preservedReason = pendingPayload?.reason || activeDraft?.reason;

            const requestBody: AiChatRequestPayload = {
                message: trimmed,
                context: historyMessages,
                pendingSpecialtyId: pendingPayload?.pendingSpecialtyId ?? pendingPayload?.specialtyId ?? activeDraft?.specialtyId,
                pendingDoctorId: pendingPayload?.pendingDoctorId ?? pendingPayload?.doctorId ?? activeDraft?.doctorId,
                pendingSlotId: pendingPayload?.pendingSlotId ?? pendingPayload?.slotId ?? activeDraft?.slotId,
                pendingSlotDate: pendingPayload?.pendingSlotDate ?? pendingPayload?.slotDate ?? activeDraft?.slotDate,
                reason: preservedReason
            };

            const res = await axiosClient.post<AiChatRequestPayload, ApiResponse<AiChatResponse>>("/ai/chat", requestBody);

            if (res.success && res.data) {
                const data = res.data;
                const aiMsg: ChatMessage = {
                    role: "model",
                    content: data.message || data.reply || "",
                    urgency: data.urgency,
                    safetyNotice: data.safetyNotice,
                    suggestions: data.specialtySuggestions || data.suggestedSpecialties || [],
                    actions: data.actions || [],
                    bookingDraft: data.bookingDraft,
                    missingFields: data.missingFields || []
                };

                if (data.bookingDraft) {
                    const mergedDraft: AiBookingDraft = {
                        ...data.bookingDraft,
                        reason: data.bookingDraft.reason || preservedReason || activeDraft?.reason
                    };
                    setActiveDraft(mergedDraft);
                    aiMsg.bookingDraft = mergedDraft;
                }

                setMessages(prev => [...prev, aiMsg]);
            } else {
                throw new Error("Invalid response");
            }
        } catch (err: unknown) {
            const apiErr = err as { errorCode?: string; message?: string; response?: { data?: { errorCode?: string; message?: string } } };
            const errorCode = apiErr?.response?.data?.errorCode || apiErr?.errorCode;
            const message = apiErr?.response?.data?.message || apiErr?.message || "";

            if (errorCode === "TOO_MANY_REQUESTS" || message.includes("quá nhiều")) {
                setMessages(prev => [...prev, {
                    role: "model",
                    content: "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau 1 phút.",
                    urgency: "ROUTINE"
                }]);
            } else {
                setMessages(prev => [...prev, {
                    role: "model",
                    content: "Xin lỗi, hệ thống AI đang bận hoặc gặp sự cố kết nối. Bạn có thể chọn chuyên khoa và đặt lịch trực tiếp qua trang Đặt lịch khám.",
                    urgency: "ROUTINE",
                    actions: [
                        {
                            id: "act-fallback-book",
                            type: "StartBooking",
                            label: "Mở trang Đặt lịch khám",
                            style: "primary",
                            requiresAuthentication: false,
                            requiresConfirmation: false,
                            payload: { targetUrl: "/patient/book" }
                        }
                    ]
                }]);
            }
        } finally {
            setLoading(false);
        }
    };

    const handleActionClick = async (action: AiAction): Promise<void> => {
        switch (action.type) {
            case "ViewSpecialty": {
                if (action.payload.specialtyId) {
                    setPendingSpecialtyId(action.payload.specialtyId);
                    navigate(`/patient/book?specialtyId=${action.payload.specialtyId}`);
                    onNavigate?.();
                } else if (action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)) {
                    navigate(action.payload.targetUrl);
                    onNavigate?.();
                }
                break;
            }

            case "ViewDoctors": {
                const targetUrl = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : (action.payload.specialtyId
                        ? `/doctors?specialtyId=${action.payload.specialtyId}`
                        : "/doctors");
                navigate(targetUrl);
                onNavigate?.();
                break;
            }

            case "ViewAvailableSlots": {
                const specId = action.payload.specialtyId ?? activeDraft?.specialtyId;
                const docId = action.payload.doctorId ?? activeDraft?.doctorId;
                const slotDate = action.payload.slotDate ?? activeDraft?.slotDate;

                await handleSendMessage(
                    `Xem lịch trống khả dụng`,
                    {
                        pendingSpecialtyId: specId,
                        pendingDoctorId: docId,
                        pendingSlotDate: slotDate,
                        reason: activeDraft?.reason
                    }
                );
                break;
            }

            case "StartBooking": {
                const url = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : (action.payload.specialtyId
                        ? `/patient/book?specialtyId=${action.payload.specialtyId}`
                        : "/patient/book");
                navigate(url);
                onNavigate?.();
                break;
            }

            case "SelectDoctor": {
                const nextDraft: AiBookingDraft = {
                    ...activeDraft,
                    specialtyId: action.payload.specialtyId,
                    specialtyName: action.payload.specialtyName || activeDraft?.specialtyName,
                    doctorId: action.payload.doctorId,
                    doctorName: action.payload.doctorName,
                    isComplete: false,
                    reason: activeDraft?.reason
                };
                setActiveDraft(nextDraft);

                await handleSendMessage(
                    `Tôi muốn đặt khám với bác sĩ ${action.payload.doctorName || ""}`,
                    {
                        pendingSpecialtyId: action.payload.specialtyId,
                        pendingDoctorId: action.payload.doctorId,
                        pendingSlotDate: action.payload.slotDate || activeDraft?.slotDate,
                        reason: activeDraft?.reason
                    }
                );
                break;
            }

            case "SelectSlot": {
                const nextDraft: AiBookingDraft = {
                    ...activeDraft,
                    specialtyId: action.payload.specialtyId ?? activeDraft?.specialtyId,
                    specialtyName: action.payload.specialtyName ?? activeDraft?.specialtyName,
                    doctorId: action.payload.doctorId ?? activeDraft?.doctorId,
                    doctorName: action.payload.doctorName ?? activeDraft?.doctorName,
                    slotId: action.payload.slotId,
                    slotDate: action.payload.slotDate,
                    startTime: action.payload.startTime,
                    endTime: action.payload.endTime,
                    isComplete: true,
                    reason: action.payload.reason || activeDraft?.reason
                };
                setActiveDraft(nextDraft);

                await handleSendMessage(
                    `Tôi chọn khung giờ ${action.payload.startTime} ngày ${formatVietnameseDate(action.payload.slotDate)}`,
                    {
                        pendingSpecialtyId: nextDraft.specialtyId,
                        pendingDoctorId: nextDraft.doctorId,
                        pendingSlotId: action.payload.slotId,
                        pendingSlotDate: action.payload.slotDate,
                        reason: nextDraft.reason
                    }
                );
                break;
            }

            case "ReviewBooking": {
                const specId = action.payload.specialtyId || activeDraft?.specialtyId;
                const docId = action.payload.doctorId || activeDraft?.doctorId;
                const slotId = action.payload.slotId || activeDraft?.slotId;
                const slotDate = action.payload.slotDate || activeDraft?.slotDate;
                const startTime = action.payload.startTime || activeDraft?.startTime;
                const endTime = action.payload.endTime || activeDraft?.endTime;
                const specName = action.payload.specialtyName || activeDraft?.specialtyName;
                const docName = action.payload.doctorName || activeDraft?.doctorName;
                const reason = action.payload.reason?.trim() || activeDraft?.reason?.trim();

                if (!specId || !docId || !slotId || !slotDate || !startTime || !reason) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Thông tin đặt lịch chưa đầy đủ (thiếu chuyên khoa, bác sĩ, khung giờ hoặc lý do khám). Vui lòng chọn đầy đủ thông tin trước khi xác nhận.",
                        urgency: "ROUTINE"
                    }]);
                    break;
                }

                const formattedDate = formatVietnameseDate(slotDate);
                const reviewMsg: ChatMessage = {
                    role: "model",
                    content: `📋 **Thông tin xác nhận lịch hẹn:**\n- **Chuyên khoa:** ${specName || "Chuyên khoa"}\n- **Bác sĩ:** ${docName || "Bác sĩ"}\n- **Thời gian:** ${startTime} ngày ${formattedDate}\n- **Lý do khám:** ${reason}\n\nBạn vui lòng xác nhận để hoàn tất đặt lịch.`,
                    urgency: "ROUTINE",
                    actions: [
                        {
                            id: "act-confirm-from-review",
                            type: "ConfirmBooking",
                            label: "Xác nhận đặt lịch",
                            style: "primary",
                            requiresAuthentication: true,
                            requiresConfirmation: true,
                            payload: {
                                specialtyId: specId,
                                specialtyName: specName,
                                doctorId: docId,
                                doctorName: docName,
                                slotId: slotId,
                                slotDate: slotDate,
                                startTime: startTime,
                                endTime: endTime || "",
                                reason: reason
                            }
                        }
                    ]
                };
                setMessages(prev => [...prev, reviewMsg]);
                break;
            }

            case "ConfirmBooking": {
                if (submittingBooking) return;
                const slotId = action.payload.slotId || activeDraft?.slotId;
                const docId = action.payload.doctorId || activeDraft?.doctorId;
                const specId = action.payload.specialtyId || activeDraft?.specialtyId;
                const slotDate = action.payload.slotDate || activeDraft?.slotDate;
                const startTime = action.payload.startTime || activeDraft?.startTime;
                const docName = action.payload.doctorName || activeDraft?.doctorName;
                const reason = (action.payload.reason || activeDraft?.reason || "").trim();

                if (!slotId || !docId || !specId || !slotDate || !startTime) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Thông tin đặt lịch chưa đầy đủ (thiếu bác sĩ hoặc khung giờ). Vui lòng kiểm tra lại.",
                        urgency: "ROUTINE"
                    }]);
                    return;
                }

                if (!reason || reason.length < 10 || reason.length > 500) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Lý do khám phải từ 10 đến 500 ký tự. Vui lòng nhập lý do khám hoặc mô tả triệu chứng trước khi xác nhận đặt lịch.",
                        urgency: "ROUTINE"
                    }]);
                    return;
                }

                setSubmittingBooking(true);
                try {
                    const bookPayload: CreateAppointmentPayload = {
                        doctorId: docId,
                        specialtyId: specId,
                        appointmentSlotId: slotId,
                        reason
                    };

                    const bookRes = await axiosClient.post<CreateAppointmentPayload, ApiResponse<AppointmentEntityDto>>("/appointments", bookPayload);

                    if (bookRes.success && bookRes.data) {
                        const apt = bookRes.data;
                        const formattedDate = formatVietnameseDate(slotDate);
                        const successMsg: ChatMessage = {
                            role: "model",
                            content: `🎉 **Đặt lịch khám thành công!**\n- **Mã cuộc hẹn:** ${apt.appointmentCode || apt.id}\n- **Bác sĩ:** ${docName || apt.doctorName || "Bác sĩ phụ trách"}\n- **Thời gian:** ${startTime || apt.startTime} ngày ${formattedDate}\n- **Lý do khám:** ${reason}\n\nCuộc hẹn của bạn đã được lưu vào hệ thống phòng khám.`,
                            actions: [
                                {
                                    id: "act-view-created-apt",
                                    type: "ViewMyAppointments",
                                    label: "Xem danh sách lịch hẹn của tôi",
                                    style: "primary",
                                    requiresAuthentication: true,
                                    requiresConfirmation: false,
                                    payload: { targetUrl: "/patient/appointments" }
                                }
                            ]
                        };
                        setMessages(prev => [...prev, successMsg]);
                        setActiveDraft(null);
                    }
                } catch (err: unknown) {
                    const apiErr = err as { response?: { data?: { errorCode?: string; message?: string } }; errorCode?: string; message?: string };
                    const errorCode = apiErr?.response?.data?.errorCode || apiErr?.errorCode;

                    if (errorCode === "SLOT_ALREADY_BOOKED") {
                        const conflictNotice: ChatMessage = {
                            role: "model",
                            content: "⚠️ **Khung giờ này vừa có bệnh nhân khác đặt trước.** Khung giờ đã được cập nhật, thông tin triệu chứng của bạn vẫn được lưu giữ. Vui lòng chọn khung giờ khác bên dưới:",
                            urgency: "ROUTINE"
                        };

                        await handleSendMessage(
                            `Xem các lịch trống khác của bác sĩ ${docName || ""}`,
                            {
                                pendingSpecialtyId: specId,
                                pendingDoctorId: docId,
                                pendingSlotDate: slotDate,
                                reason
                            },
                            conflictNotice
                        );
                    } else {
                        const errorNotice = apiErr?.response?.data?.message || apiErr?.message || "Đặt lịch không thành công. Vui lòng thử lại.";
                        setMessages(prev => [...prev, {
                            role: "model",
                            content: `⚠️ ${errorNotice}`,
                            urgency: "ROUTINE"
                        }]);
                    }
                } finally {
                    setSubmittingBooking(false);
                }
                break;
            }

            case "ChangePreferredDate": {
                const nextDate = action.payload.slotDate;
                const formattedNext = formatVietnameseDate(nextDate);
                const preservedReason = action.payload.reason || activeDraft?.reason;

                if (activeDraft) {
                    setActiveDraft({
                        ...activeDraft,
                        slotDate: nextDate,
                        slotId: undefined,
                        startTime: undefined,
                        endTime: undefined,
                        isComplete: false,
                        reason: preservedReason
                    });
                }

                await handleSendMessage(
                    `Tôi muốn xem lịch khám vào ngày ${formattedNext}`,
                    {
                        pendingSpecialtyId: action.payload.specialtyId ?? activeDraft?.specialtyId,
                        pendingDoctorId: action.payload.doctorId ?? activeDraft?.doctorId,
                        pendingSlotDate: nextDate,
                        reason: preservedReason
                    }
                );
                break;
            }

            case "ViewMyAppointments":
            case "OpenAppointmentDetail":
            case "RequestReschedule":
            case "RequestCancellation": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "/patient/appointments";
                navigate(target);
                onNavigate?.();
                break;
            }

            case "ViewDiagnosticResults": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "/patient/diagnostic-results";
                navigate(target);
                onNavigate?.();
                break;
            }

            case "ViewPrescriptions": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "/patient/prescriptions";
                navigate(target);
                onNavigate?.();
                break;
            }

            case "ViewBills": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "/patient/invoices";
                navigate(target);
                onNavigate?.();
                break;
            }

            case "ContactReception": {
                const phone = action.payload.phoneNumber?.trim();
                const reason = action.payload.reason?.trim();
                
                let content: string;
                if (phone) {
                    content = `📞 **Thông tin Quầy Tiếp Đón & Lễ Tân:**\n- **Hotline hỗ trợ:** ${phone}${reason ? `\n- **Ghi chú:** ${reason}` : ""}\n\nNếu cần hỗ trợ thêm, bạn có thể liên hệ số điện thoại trên.`;
                } else {
                    content = `📞 **Thông tin Quầy Tiếp Đón & Lễ Tân:**\nThông tin liên hệ lễ tân chưa được cấu hình trong hệ thống.`;
                }

                const receptionMsg: ChatMessage = {
                    role: "model",
                    content,
                    urgency: "ROUTINE"
                };
                setMessages(prev => [...prev, receptionMsg]);
                break;
            }

            case "ManualSpecialtySelection": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "/patient/book";
                navigate(target);
                onNavigate?.();
                break;
            }

            case "CallEmergency": {
                const target = action.payload.targetUrl && isSafeClientRoute(action.payload.targetUrl)
                    ? action.payload.targetUrl
                    : "tel:115";
                window.location.href = target;
                break;
            }

            default: {
                const _exhaustiveCheck: never = action;
                console.warn("Unhandled action type:", _exhaustiveCheck);
                break;
            }
        }
    };

    return {
        input,
        setInput,
        loading,
        submittingBooking,
        errorMsg,
        setErrorMsg,
        messages,
        activeDraft,
        clearChat,
        handleSendMessage,
        handleActionClick,
        formatVietnameseDate
    };
};

