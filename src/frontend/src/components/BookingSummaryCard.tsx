import { Calendar } from "lucide-react";
import type { AiBookingDraft } from "../types/ai";
import styles from "./MedicalChatWidget.module.css";

export const BookingSummaryCard = ({ draft, formatVietnameseDate }: { draft: AiBookingDraft; formatVietnameseDate: (date?: string) => string }) => (
<div className={styles.cardContainer}>
                                            <div className={styles.bookingSummaryCard}>
                                                <h4 className={styles.bookingSummaryTitle}>
                                                    <Calendar size={18} color="var(--chat-accent)" />
                                                    Tóm tắt thông tin đặt lịch
                                                </h4>
                                                <div className={styles.summaryTable}>
                                                    <div className={styles.summaryRow}>
                                                        <span className={styles.summaryLabel}>Chuyên khoa:</span>
                                                        <span className={styles.summaryValue}>{draft.specialtyName}</span>
                                                    </div>
                                                    <div className={styles.summaryRow}>
                                                        <span className={styles.summaryLabel}>Bác sĩ:</span>
                                                        <span className={styles.summaryValue}>{draft.doctorName}</span>
                                                    </div>
                                                    <div className={styles.summaryRow}>
                                                        <span className={styles.summaryLabel}>Ngày khám:</span>
                                                        <span className={styles.summaryValue}>{formatVietnameseDate(draft.slotDate)}</span>
                                                    </div>
                                                    <div className={styles.summaryRow}>
                                                        <span className={styles.summaryLabel}>Khung giờ:</span>
                                                        <span className={styles.summaryValue}>{draft.startTime} - {draft.endTime}</span>
                                                    </div>
                                                    <div className={styles.summaryRow}>
                                                        <span className={styles.summaryLabel}>Lý do khám:</span>
                                                        <span className={styles.summaryValue}>{draft.reason}</span>
                                                    </div>
                                                </div>
                                            </div>
                                        </div>
);
