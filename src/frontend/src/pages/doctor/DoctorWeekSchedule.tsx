import React from 'react';
import type { DoctorScheduleDayDto } from '../../types';
import { formatDateOnly } from '../../utils/formatters';
import styles from './DoctorWeekSchedule.module.css';

interface DoctorWeekScheduleProps {
    days: DoctorScheduleDayDto[];
}

const hhmm = (time: string) => time.substring(0, 5);

export const DoctorWeekSchedule: React.FC<DoctorWeekScheduleProps> = ({ days }) => (
    <ol className={styles.week} aria-label="Lịch trực trong tuần">
        {days.map(day => (
            <li key={day.date} className={styles.day} data-testid={`schedule-day-${day.date}`}>
                <div className={styles.dayLabel}>
                    <span className={styles.dayName}>{day.dayOfWeekName}</span>
                    <span className={styles.dayDate}>{formatDateOnly(day.date).slice(0, 5)}</span>
                </div>
                {day.shifts.length === 0 ? (
                    <span className={styles.offDay}>Nghỉ</span>
                ) : (
                    <div className={styles.shifts}>
                        {day.shifts.map(shift => (
                            <div key={shift.scheduleId} className={styles.shift}>
                                <div className={styles.shiftHeading}>
                                    <span className={styles.shiftName}>{shift.shiftName}: {hhmm(shift.startTime)} - {hhmm(shift.endTime)}</span>
                                    <span className={styles.slotCount}>
                                        {shift.slots.filter(s => s.isBooked).length}/{shift.slots.length} khung giờ đã được đặt
                                    </span>
                                </div>
                                <div className={styles.slots}>
                                    {shift.slots.map(slot => (
                                        <div key={slot.slotId} className={slot.isBooked ? `${styles.slot} ${styles.slotBooked}` : styles.slot}>
                                            <div className={styles.slotTime}>
                                                <span>{hhmm(slot.startTime)} - {hhmm(slot.endTime)}</span>
                                                <span className={slot.isBooked ? styles.slotStatusBooked : styles.slotStatusFree}>
                                                    {slot.isBooked ? 'Đã đặt' : 'Trống'}
                                                </span>
                                            </div>
                                            {slot.isBooked && (
                                                <div className={styles.slotPatient}>
                                                    <div className={styles.slotPatientName}>{slot.patientName}</div>
                                                    {slot.appointmentCode && <div>{slot.appointmentCode}</div>}
                                                </div>
                                            )}
                                        </div>
                                    ))}
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </li>
        ))}
    </ol>
);
