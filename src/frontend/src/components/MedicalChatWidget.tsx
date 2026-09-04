import React, { useState, useRef, useEffect } from "react";
import { createPortal } from "react-dom";
import { useLocation, useNavigate } from "react-router-dom";
import { useChatContext } from "../contexts/ChatContext";
import axiosClient from "../api/axiosClient";
import styles from "./MedicalChatWidget.module.css";
import { MessageCircle, X, Trash2, Send, AlertTriangle, ArrowRight, Minus, Stethoscope } from "lucide-react";
import type { ApiResponse } from "../types";
import { useAuth } from "../auth/AuthContext";

export interface ChatMessage {
    role: "user" | "model";
    content: string;
    urgency?: string;
    suggestions?: any[];
}

const PatientMedicalChatWidget: React.FC = () => {
    const [isOpen, setIsOpen] = useState(false);
    const { messages, setMessages, setPendingSpecialtyId } = useChatContext();
    const [input, setInput] = useState("");
    const [loading, setLoading] = useState(false);
    const [errorMsg, setErrorMsg] = useState("");
    
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const navigate = useNavigate();
    const location = useLocation();

    useEffect(() => {
        if (isOpen) {
            messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
        }
    }, [messages, isOpen]);

    useEffect(() => {
        const handleEsc = (e: KeyboardEvent) => {
            if (e.key === "Escape" && isOpen) {
                setIsOpen(false);
            }
        };
        window.addEventListener("keydown", handleEsc);
        return () => window.removeEventListener("keydown", handleEsc);
    }, [isOpen]);

    const handleClear = () => {
        setMessages([{
            role: "model",
            content: "Chào bạn, tôi là trợ lý y tế AI của ClinicCare. Tôi có thể hỗ trợ thông tin sức khỏe tham khảo và gợi ý chuyên khoa phù hợp. Bạn đang gặp triệu chứng hoặc cần tư vấn về vấn đề sức khỏe nào ạ?"
        }]);
        setErrorMsg("");
    };

    const handleSend = async () => {
        if (!input.trim() || loading) return;
        if (input.length > 500) {
            setErrorMsg("Tin nhắn quá dài (tối đa 500 ký tự).");
            return;
        }

        const userMsg: ChatMessage = { role: "user", content: input.trim() };
        const newMessages = [...messages, userMsg];
        const historyMessages = newMessages.slice(-7, -1).map(m => ({ role: m.role, content: m.content }));
        
        setMessages(newMessages);
        setInput("");
        setErrorMsg("");
        setLoading(true);

        try {
            const res = await axiosClient.post<any, ApiResponse<any>>("/ai/chat", {
                message: userMsg.content,
                context: historyMessages
            });

            if (res.success && res.data) {
                const aiMsg: ChatMessage = {
                    role: "model",
                    content: res.data.reply,
                    urgency: res.data.urgency,
                    suggestions: res.data.suggestedSpecialties || []
                };
                setMessages(prev => [...prev, aiMsg]);
            } else {
                throw new Error("Invalid response");
            }
        } catch (err: any) {
            if (err?.errorCode === "TOO_MANY_REQUESTS" || err?.message?.includes("quá nhiều")) {
                setMessages(prev => [...prev, {
                    role: "model",
                    content: "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau 1 phút.",
                    urgency: "ROUTINE"
                }]);
            } else {
                setMessages(prev => [...prev, {
                    role: "model",
                    content: "Xin lỗi, hệ thống AI đang gặp sự cố. Vui lòng thử lại sau.",
                    urgency: "ROUTINE"
                }]);
            }
        } finally {
            setLoading(false);
        }
    };

    const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
        if (e.nativeEvent.isComposing) return;
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            e.stopPropagation();
            handleSend();
        }
    };

    const handleSelectSpecialty = (id: number) => {
        if (location.pathname === "/patient/book") {
            setPendingSpecialtyId(id);
        } else {
            navigate("/patient/book?specialtyId=" + id);
        }
        setIsOpen(false);
    };

    const widgetContent = (
        <div className={styles.widgetContainer}>
            {!isOpen && (
                <button 
                    className={styles.launcher} 
                    onClick={() => setIsOpen(true)}
                    aria-label="Mở chat y tế"
                    type="button"
                >
                    <MessageCircle size={28} />
                </button>
            )}

            {isOpen && (
                <div className={styles.chatWindow}>
                    <div className={styles.header}>
                        <div className={styles.headerTitle}>
                            <Stethoscope size={22} />
                            ClinicCare Assistant
                        </div>
                        <div className={styles.headerActions}>
                            <button type="button" className={styles.iconBtn} onClick={handleClear} title="Xóa lịch sử">
                                <Trash2 size={18} />
                            </button>
                            <button type="button" className={styles.iconBtn} onClick={() => setIsOpen(false)} title="Thu nhỏ">
                                <Minus size={18} />
                            </button>
                            <button type="button" className={styles.iconBtn} onClick={() => setIsOpen(false)} title="Đóng">
                                <X size={18} />
                            </button>
                        </div>
                    </div>

                    <div className={styles.messageArea}>
                        {messages.map((msg, idx) => (
                            <div key={idx} className={`${styles.messageRow} ${msg.role === "user" ? styles.rowUser : styles.rowModel}`}>
                                <div className={`${styles.bubble} ${msg.role === "user" ? styles.bubbleUser : styles.bubbleModel}`}>
                                    {msg.content}
                                    
                                    {msg.urgency === "EMERGENCY" && (
                                        <div className={styles.emergencyBanner}>
                                            <AlertTriangle size={20} style={{ flexShrink: 0 }} />
                                            <span>Đây có thể là tình huống khẩn cấp. Hãy gọi 115 hoặc đến cơ sở cấp cứu gần nhất ngay.</span>
                                        </div>
                                    )}

                                    {msg.suggestions && msg.suggestions.length > 0 && msg.urgency !== "EMERGENCY" && (
                                        <div className={styles.suggestions}>
                                            {msg.suggestions.map(s => (
                                                <div key={s.specialtyId} className={styles.suggestCard}>
                                                    <h4>{s.specialtyName}</h4>
                                                    <button 
                                                        type="button"
                                                        className="btn-secondary"
                                                        onClick={() => handleSelectSpecialty(s.specialtyId)}
                                                    >
                                                        Đặt lịch khoa này <ArrowRight size={14} style={{ verticalAlign: "middle" }}/>
                                                    </button>
                                                </div>
                                            ))}
                                        </div>
                                    )}
                                </div>
                            </div>
                        ))}
                        {loading && (
                            <div className={`${styles.messageRow} ${styles.rowModel}`}>
                                <div className={`${styles.bubble} ${styles.bubbleModel}`}>
                                    <div style={{ display: "flex", gap: "4px", padding: "4px" }}>
                                        <div style={{ width: "6px", height: "6px", borderRadius: "50%", backgroundColor: "var(--c-muted, #94a3b8)", animation: "pulse 1.5s infinite" }} />
                                        <div style={{ width: "6px", height: "6px", borderRadius: "50%", backgroundColor: "var(--c-muted, #94a3b8)", animation: "pulse 1.5s infinite 0.2s" }} />
                                        <div style={{ width: "6px", height: "6px", borderRadius: "50%", backgroundColor: "var(--c-muted, #94a3b8)", animation: "pulse 1.5s infinite 0.4s" }} />
                                    </div>
                                </div>
                            </div>
                        )}
                        <div ref={messagesEndRef} />
                    </div>

                    <div className={styles.inputArea}>
                        <textarea
                            className={styles.textarea}
                            placeholder="Nhập triệu chứng hoặc câu hỏi..."
                            value={input}
                            onChange={(e) => setInput(e.target.value)}
                            onKeyDown={handleKeyDown}
                            rows={1}
                            maxLength={500}
                        />
                        <button 
                            type="button"
                            className={styles.sendBtn} 
                            onClick={handleSend}
                            disabled={!input.trim() || loading}
                        >
                            <Send size={18} />
                        </button>
                    </div>
                    {errorMsg && <div className={styles.errorText}>{errorMsg}</div>}
                </div>
            )}
        </div>
    );

    return createPortal(widgetContent, document.body);
};

export const MedicalChatWidget: React.FC = () => {
    const { user } = useAuth();
    if (user?.role !== "Patient") return null;
    return <PatientMedicalChatWidget />;
};