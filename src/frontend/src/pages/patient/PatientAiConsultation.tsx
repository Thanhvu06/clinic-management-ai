import React, { useState, useRef, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import axiosClient from "../../api/axiosClient";
import { Trash2, Send, AlertTriangle, ArrowRight, Stethoscope } from "lucide-react";
import type { ApiResponse } from "../../types";
import { Breadcrumb } from "../../components/Breadcrumb";

import { useChatContext } from "../../contexts/ChatContext";

export interface ChatMessage {
    role: "user" | "model";
    content: string;
    urgency?: string;
    suggestions?: any[];
}

export const PatientAiConsultation: React.FC = () => {
    const { messages, setMessages } = useChatContext();
    const [input, setInput] = useState("");
    const [loading, setLoading] = useState(false);
    const [errorMsg, setErrorMsg] = useState("");
    
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const navigate = useNavigate();

    useEffect(() => {
        messagesEndRef.current?.scrollIntoView({ behavior: "smooth" });
    }, [messages, loading]);

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
        navigate("/patient/book?specialtyId=" + id);
    };

    return (
        <div style={{ maxWidth: 1000, margin: '0 auto', display: 'flex', flexDirection: 'column', height: 'calc(100vh - 120px)' }}>
            <Breadcrumb items={[
                { label: 'Trang chủ', path: '/patient' },
                { label: 'Tư vấn AI' }
            ]} />
            
            <div className="card" style={{ flex: 1, display: 'flex', flexDirection: 'column', padding: 0, overflow: 'hidden' }}>
                <div style={{ padding: '16px 24px', backgroundColor: 'var(--c-primary)', color: 'white', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '12px', fontSize: '1.25rem', fontWeight: 600 }}>
                        <Stethoscope size={24} />
                        ClinicCare AI Assistant
                    </div>
                    <button 
                        type="button" 
                        onClick={handleClear} 
                        title="Xóa lịch sử"
                        style={{ background: 'rgba(255,255,255,0.2)', border: 'none', color: 'white', padding: '8px', borderRadius: 'var(--radius-md)', cursor: 'pointer', display: 'flex', alignItems: 'center', gap: '8px' }}
                    >
                        <Trash2 size={16} /> Làm mới
                    </button>
                </div>

                <div style={{ flex: 1, overflowY: 'auto', padding: '24px', backgroundColor: '#f8fafc', display: 'flex', flexDirection: 'column', gap: '16px' }}>
                    {messages.map((msg, idx) => (
                        <div key={idx} style={{ display: 'flex', justifyContent: msg.role === 'user' ? 'flex-end' : 'flex-start' }}>
                            <div style={{ 
                                maxWidth: '75%', 
                                padding: '12px 16px', 
                                borderRadius: '16px',
                                borderBottomRightRadius: msg.role === 'user' ? '4px' : '16px',
                                borderBottomLeftRadius: msg.role === 'model' ? '4px' : '16px',
                                backgroundColor: msg.role === 'user' ? 'var(--c-primary)' : 'white',
                                color: msg.role === 'user' ? 'white' : 'var(--c-text)',
                                boxShadow: '0 2px 4px rgba(0,0,0,0.05)',
                                border: msg.role === 'model' ? '1px solid var(--c-border)' : 'none',
                                lineHeight: 1.5
                            }}>
                                <div style={{ whiteSpace: 'pre-wrap' }}>{msg.content}</div>
                                
                                {msg.urgency === "EMERGENCY" && (
                                    <div style={{ marginTop: '12px', backgroundColor: '#FEF2F2', border: '1px solid #FECACA', borderRadius: '8px', padding: '12px', display: 'flex', gap: '12px', color: '#B91C1C' }}>
                                        <AlertTriangle size={24} style={{ flexShrink: 0 }} />
                                        <span style={{ fontSize: '0.9rem', fontWeight: 500 }}>Đây có thể là tình huống khẩn cấp. Hãy gọi 115 hoặc đến cơ sở cấp cứu gần nhất ngay.</span>
                                    </div>
                                )}

                                {msg.suggestions && msg.suggestions.length > 0 && msg.urgency !== "EMERGENCY" && (
                                    <div style={{ marginTop: '16px', display: 'flex', flexDirection: 'column', gap: '8px' }}>
                                        <p style={{ margin: '0 0 4px', fontSize: '0.85rem', fontWeight: 600, color: 'var(--c-navy)' }}>Gợi ý chuyên khoa:</p>
                                        {msg.suggestions.map(s => (
                                            <div key={s.specialtyId} style={{ backgroundColor: 'var(--c-bg)', border: '1px solid var(--c-border)', borderRadius: '8px', padding: '12px', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                                                <h4 style={{ margin: 0, color: 'var(--c-navy)', fontSize: '0.95rem' }}>{s.specialtyName}</h4>
                                                <button 
                                                    type="button"
                                                    className="btn-primary"
                                                    onClick={() => handleSelectSpecialty(s.specialtyId)}
                                                    style={{ padding: '6px 12px', fontSize: '0.85rem' }}
                                                >
                                                    Đặt lịch <ArrowRight size={14} style={{ verticalAlign: "middle" }}/>
                                                </button>
                                            </div>
                                        ))}
                                    </div>
                                )}
                            </div>
                        </div>
                    ))}
                    {loading && (
                        <div style={{ display: 'flex', justifyContent: 'flex-start' }}>
                            <div style={{ padding: '16px', borderRadius: '16px', backgroundColor: 'white', border: '1px solid var(--c-border)' }}>
                                <div style={{ display: "flex", gap: "6px" }}>
                                    <div style={{ width: "8px", height: "8px", borderRadius: "50%", backgroundColor: "var(--c-muted)", animation: "pulse 1.5s infinite" }} />
                                    <div style={{ width: "8px", height: "8px", borderRadius: "50%", backgroundColor: "var(--c-muted)", animation: "pulse 1.5s infinite 0.2s" }} />
                                    <div style={{ width: "8px", height: "8px", borderRadius: "50%", backgroundColor: "var(--c-muted)", animation: "pulse 1.5s infinite 0.4s" }} />
                                </div>
                            </div>
                        </div>
                    )}
                    <div ref={messagesEndRef} />
                </div>

                <div style={{ padding: '20px 24px', backgroundColor: 'white', borderTop: '1px solid var(--c-border)' }}>
                    <div style={{ display: 'flex', gap: '12px', alignItems: 'center', backgroundColor: '#f1f5f9', padding: '8px 16px', borderRadius: '24px', border: '1px solid var(--c-border-light)' }}>
                        <textarea
                            style={{ flex: 1, border: 'none', background: 'transparent', outline: 'none', resize: 'none', padding: '8px 0', fontSize: '1rem', color: 'var(--c-text)', maxHeight: '120px', minHeight: '24px' }}
                            placeholder="Nhập triệu chứng hoặc câu hỏi..."
                            value={input}
                            onChange={(e) => setInput(e.target.value)}
                            onKeyDown={handleKeyDown}
                            rows={1}
                            maxLength={500}
                        />
                        <button 
                            type="button"
                            onClick={handleSend}
                            disabled={!input.trim() || loading}
                            style={{ width: '40px', height: '40px', borderRadius: '50%', backgroundColor: input.trim() && !loading ? 'var(--c-primary)' : 'var(--c-border)', color: 'white', border: 'none', display: 'flex', alignItems: 'center', justifyContent: 'center', cursor: input.trim() && !loading ? 'pointer' : 'not-allowed', transition: 'background-color 0.2s' }}
                        >
                            <Send size={18} />
                        </button>
                    </div>
                    {errorMsg && <div style={{ color: 'var(--c-danger)', fontSize: '0.85rem', marginTop: '8px', paddingLeft: '16px' }}>{errorMsg}</div>}
                </div>
            </div>
            
            <style>{`
                @keyframes pulse {
                    0%, 100% { opacity: 0.4; transform: scale(0.8); }
                    50% { opacity: 1; transform: scale(1.2); }
                }
            `}</style>
        </div>
    );
};
