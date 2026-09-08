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

        const historyMessages = newMessages
            .slice(-9, -1)
            .map(m => ({ role: m.role, content: m.content }));

        setMessages(newMessages);
        setInput("");
        setErrorMsg("");
        setLoading(true);

        try {
            const preservedReason = pendingPayload?.reason || activeDraft?.reason;

            const requestBody = {
                message: trimmed,
                context: historyMessages,
                pendingSpecialtyId: pendingPayload?.pendingSpecialtyId ?? pendingPayload?.specialtyId ?? activeDraft?.specialtyId,
                pendingDoctorId: pendingPayload?.pendingDoctorId ?? pendingPayload?.doctorId ?? activeDraft?.doctorId,
                pendingSlotId: pendingPayload?.pendingSlotId ?? pendingPayload?.slotId ?? activeDraft?.slotId,
                pendingSlotDate: pendingPayload?.pendingSlotDate ?? pendingPayload?.slotDate ?? activeDraft?.slotDate,
                reason: preservedReason
            };

            const res = await axiosClient.post<any, ApiResponse<AiChatResponse>>("/ai/chat", requestBody);

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
                } else if (action.payload.targetUrl) {
                    navigate(action.payload.targetUrl);
                    onNavigate?.();
                }
                break;
            }

            case "ViewDoctors": {
                const targetUrl = action.payload.targetUrl || (action.payload.specialtyId
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
                const url = action.payload.targetUrl || (action.payload.specialtyId
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
                const formattedDate = formatVietnameseDate(action.payload.slotDate || activeDraft?.slotDate);
                const reviewMsg: ChatMessage = {
                    role: "model",
                    content: `📋 **Thông tin xác nhận lịch hẹn:**\n- **Chuyên khoa:** ${action.payload.specialtyName || activeDraft?.specialtyName || "N/A"}\n- **Bác sĩ:** ${action.payload.doctorName || activeDraft?.doctorName || "N/A"}\n- **Thời gian:** ${action.payload.startTime || activeDraft?.startTime} ngày ${formattedDate}\n- **Lý do khám:** ${action.payload.reason || activeDraft?.reason || "Khám sức khỏe"}\n\nBạn vui lòng xác nhận để hoàn tất đặt lịch.`,
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
                                specialtyId: action.payload.specialtyId || activeDraft?.specialtyId || 0,
                                specialtyName: action.payload.specialtyName || activeDraft?.specialtyName,
                                doctorId: action.payload.doctorId || activeDraft?.doctorId || 0,
                                doctorName: action.payload.doctorName || activeDraft?.doctorName,
                                slotId: action.payload.slotId || activeDraft?.slotId || 0,
                                slotDate: action.payload.slotDate || activeDraft?.slotDate || "",
                                startTime: action.payload.startTime || activeDraft?.startTime || "",
                                endTime: action.payload.endTime || activeDraft?.endTime || "",
                                reason: action.payload.reason || activeDraft?.reason || "Khám sức khỏe"
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
                const reason = action.payload.reason || activeDraft?.reason || "Đặt lịch qua Trợ lý ClinicCare AI";

                if (!slotId || !docId || !specId) {
                    setMessages(prev => [...prev, {
                        role: "model",
                        content: "Thông tin đặt lịch chưa đầy đủ (thiếu bác sĩ hoặc khung giờ). Vui lòng chọn lại khung giờ.",
                        urgency: "ROUTINE"
                    }]);
                    return;
                }

                setSubmittingBooking(true);
                try {
                    const bookRes = await axiosClient.post<any, ApiResponse<any>>("/appointments", {
                        doctorId: docId,
                        specialtyId: specId,
                        appointmentSlotId: slotId,
                        reason
                    });

                    if (bookRes.success && bookRes.data) {
                        const apt = bookRes.data;
                        const formattedDate = formatVietnameseDate(slotDate);
                        const successMsg: ChatMessage = {
                            role: "model",
                            content: `🎉 **Đặt lịch khám thành công!**\n- **Mã cuộc hẹn:** ${apt.appointmentCode || apt.id}\n- **Bác sĩ:** ${docName || "Bác sĩ phụ trách"}\n- **Thời gian:** ${startTime || ""} ngày ${formattedDate}\n- **Lý do khám:** ${reason}\n\nCuộc hẹn của bạn đã được lưu vào hệ thống phòng khám.`,
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
                navigate(action.payload.targetUrl || "/patient/appointments");
                onNavigate?.();
                break;
            }

            case "ViewDiagnosticResults": {
                navigate(action.payload.targetUrl || "/patient/diagnostic-results");
                onNavigate?.();
                break;
            }

            case "ViewPrescriptions": {
                navigate(action.payload.targetUrl || "/patient/prescriptions");
                onNavigate?.();
                break;
            }

            case "ViewBills": {
                // Strictly canonical /patient/invoices
                navigate(action.payload.targetUrl || "/patient/invoices");
                onNavigate?.();
                break;
            }

            case "ContactReception": {
                const phone = action.payload.phoneNumber || "1900 1234 (Nhánh 1)";
                const receptionMsg: ChatMessage = {
                    role: "model",
                    content: `📞 **Thông tin Quầy Tiếp Đón & Lễ Tân:**\n- **Hotline hỗ trợ:** ${phone}\n- **Thời gian làm việc:** Thứ 2 - Thứ 7 (07:30 - 17:30)\n- **Vị trí quầy:** Tầng trệt, sảnh chính Phòng khám ClinicCare\n\nNếu cần đổi lịch gấp hoặc hỗ trợ đặc biệt, bạn có thể gọi trực tiếp hoặc đến quầy lễ tân để được tiếp đón chu đáo.`,
                    urgency: "ROUTINE"
                };
                setMessages(prev => [...prev, receptionMsg]);
                break;
            }

            case "ManualSpecialtySelection": {
                navigate(action.payload.targetUrl || "/patient/book");
                onNavigate?.();
                break;
            }

            case "CallEmergency": {
                window.location.href = action.payload.targetUrl || "tel:115";
                break;
            }

            default: {
                // Exhaustive check
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
