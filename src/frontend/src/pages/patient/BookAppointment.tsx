import { toLocalDateString } from '../../utils/formatters';
import React, { useState, useEffect, useRef } from "react";
import { Alert, Avatar, Button, Card, Descriptions, Empty, Form, Input, Result, Spin, Steps, Tag } from 'antd';
import '../../theme/browserCompat';
import { useLocation, Link, useNavigate } from "react-router-dom";
import axiosClient from "../../api/axiosClient";
import styles from "./BookAppointment.module.css";
import type { ApiResponse } from "../../types";
import { 
    User, Clock, Stethoscope, FileText,
    ArrowRight, ArrowLeft, Calendar, Sparkles
} from "lucide-react";
import {
    useChatContext,
    readPersistedBookingAttempt,
    writePersistedBookingAttempt,
    removePersistedBookingAttempt,
    type PendingBookingAttemptRecord
} from "../../contexts/ChatContext";
import { useDialog } from "../../contexts/DialogContext";
import { useAuth } from "../../auth/AuthContext";
import { Breadcrumb } from "../../components/Breadcrumb";
import { formatDoctorName, getNextWorkingDateString, isSundayDateString } from "../../utils/doctorNameHelper";
import { isValidBookingReason, normalizeBookingReason } from "../../hooks/useAiBookingFlow";

interface Specialty {
    id: number;
    specialtyCode: string;
    specialtyName: string;
    description: string;
    aiEnabled: boolean;
}

interface Doctor {
    id: number;
    fullName: string;
    academicTitle?: string;
    experienceYears?: number;
    specialtyId?: number;
    specialtyName?: string;
    description?: string;
}

interface Slot {
    slotId: number;
    doctorId: number;
    slotDate: string;
    startTime: string;
    endTime: string;
}

interface BookingSuccessData {
    appointmentId: number;
    appointmentCode: string;
    doctorName: string;
    specialtyName: string;
    slotDate: string;
    startTime: string;
    endTime: string;
    reason?: string;
}

interface AppointmentResponsePayload {
    id?: number;
    appointmentId?: number;
    appointmentCode: string;
    doctorName?: string;
    specialtyName?: string;
    startTime?: string;
    endTime?: string;
    reason?: string;
}

export const BookAppointment: React.FC = () => {
    const location = useLocation();
    const navigate = useNavigate();
    const revisitRequestIdParam = new URLSearchParams(location.search).get("revisitRequestId");
    const parsedRevisitRequestId = revisitRequestIdParam ? Number(revisitRequestIdParam) : null;
    const revisitRequestId =
        parsedRevisitRequestId &&
        Number.isSafeInteger(parsedRevisitRequestId) &&
        parsedRevisitRequestId > 0
            ? parsedRevisitRequestId
            : null;

    const {
        pendingSpecialtyId,
        setPendingSpecialtyId,
        activeDraft,
        setActiveDraft
    } = useChatContext();
    const { showAlert } = useDialog();

    const [step, setStep] = useState<1 | 2 | 3 | 4>(1);
    const [pageSpecialtyId, setPageSpecialtyId] = useState<number | "">("");
    const [pageDoctorId, setPageDoctorId] = useState<number | "">("");
    const [pageSlotDate, setPageSlotDate] = useState<string>(() => getNextWorkingDateString());
    const [pageSlotId, setPageSlotId] = useState<number | "">("");
    const [pageReason, setPageReason] = useState("");
    const specialtyId = activeDraft?.specialtyId ?? pageSpecialtyId;
    const doctorId = activeDraft?.doctorId ?? pageDoctorId;
    const slotDate = activeDraft?.slotDate ?? pageSlotDate;
    const slotId = activeDraft?.slotId ?? pageSlotId;
    const reason = activeDraft?.reason ?? pageReason;

    const [specialties, setSpecialties] = useState<Specialty[]>([]);
    const [doctors, setDoctors] = useState<Doctor[]>([]);
    const [slots, setSlots] = useState<Slot[]>([]);

    const [loadingSpecs, setLoadingSpecs] = useState(true);
    const [loadingDocs, setLoadingDocs] = useState(false);
    const [loadingSlots, setLoadingSlots] = useState(false);
    const [specialtiesError, setSpecialtiesError] = useState(false);
    const [doctorsError, setDoctorsError] = useState(false);
    const [slotsError, setSlotsError] = useState(false);
    const [specialtiesReloadKey, setSpecialtiesReloadKey] = useState(0);
    const [doctorsReloadKey, setDoctorsReloadKey] = useState(0);
    const [slotsReloadKey, setSlotsReloadKey] = useState(0);

    const [submitting, setSubmitting] = useState(false);
    const [fallbackFormId] = useState(() => `form_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`);
    const isSubmittingRef = useRef(false);
    const isMountedRef = useRef(true);
    const activeSubmitIdRef = useRef(0);
    const pageTurnEpochRef = useRef(1);
    const lastConfirmationAttemptRef = useRef<PendingBookingAttemptRecord | null>(null);
    const [bookingError, setBookingError] = useState<string | null>(null);
    const [successBooking, setSuccessBooking] = useState<BookingSuccessData | null>(null);

    const { user } = useAuth();
    const currentAccountKey = user ? (user.userId || (user.id !== undefined ? String(user.id) : null)) : null;
    const currentAccountKeyRef = useRef(currentAccountKey);
    const activeDraftRef = useRef(activeDraft);
    const prevDraftRef = useRef(activeDraft);
    const prevUserIdRef = useRef(currentAccountKey);

    const [uncertainAttempt, setUncertainAttempt] = useState<PendingBookingAttemptRecord | null>(() => {
        const persisted = readPersistedBookingAttempt(currentAccountKey);
        return persisted && persisted.status === "uncertain" ? persisted : null;
    });

    const currentPayloadFingerprint = `${revisitRequestId ?? "std"}_${specialtyId}_${doctorId}_${slotDate}_${slotId}_${reason.trim()}`;
    const currentPayloadFingerprintRef = useRef(currentPayloadFingerprint);

    useEffect(() => {
        const focusReason = () => document.getElementById("reason")?.focus();
        window.addEventListener("cliniccare:focus-booking-reason", focusReason);
        return () => window.removeEventListener("cliniccare:focus-booking-reason", focusReason);
    }, []);

    useEffect(() => {
        isMountedRef.current = true;
        const persisted = readPersistedBookingAttempt(currentAccountKeyRef.current);
        if (persisted) {
            lastConfirmationAttemptRef.current = persisted;
            if (persisted.status === "uncertain") {
                setUncertainAttempt(persisted);
            }
        }
        return () => {
            isMountedRef.current = false;
        };
    }, []);

    useEffect(() => {
        currentAccountKeyRef.current = currentAccountKey;
        activeDraftRef.current = activeDraft;
        const prevFingerprint = currentPayloadFingerprintRef.current;
        currentPayloadFingerprintRef.current = currentPayloadFingerprint;

        // If user edited draft/form payload while an older request was in-flight, release UI submit lock for the new draft
        if (prevFingerprint !== currentPayloadFingerprint && isSubmittingRef.current) {
            activeSubmitIdRef.current += 1;
            isSubmittingRef.current = false;
            setSubmitting(false);
        }
    }, [currentAccountKey, activeDraft, currentPayloadFingerprint]);

    // Synchronize form when an active draft is cancelled, reset, or completed
    useEffect(() => {
        const prev = prevDraftRef.current;
        prevDraftRef.current = activeDraft;

        // Only clear if transitioning from an existing active draft to null
        if (prev !== null && activeDraft === null) {
            pageTurnEpochRef.current += 1;
            activeSubmitIdRef.current += 1;
            isSubmittingRef.current = false;
            setSubmitting(false);
            lastConfirmationAttemptRef.current = null;
            setUncertainAttempt(null);
            if (currentAccountKeyRef.current) {
                removePersistedBookingAttempt(currentAccountKeyRef.current);
            }
            setPageSpecialtyId("");
            setPageDoctorId("");
            setPageSlotId("");
            setPageReason("");
            setStep(1);
        } else if (prev?.draftId && activeDraft?.draftId && prev.draftId !== activeDraft.draftId) {
            pageTurnEpochRef.current += 1;
            activeSubmitIdRef.current += 1;
            isSubmittingRef.current = false;
            setSubmitting(false);
            lastConfirmationAttemptRef.current = null;
            setUncertainAttempt(null);
            if (currentAccountKeyRef.current) {
                removePersistedBookingAttempt(currentAccountKeyRef.current);
            }
        }
    }, [activeDraft]);

    // Clear form when account changes
    useEffect(() => {
        const currentUserId = user ? (user.userId || (user.id !== undefined ? String(user.id) : null)) : null;
        if (prevUserIdRef.current !== null && prevUserIdRef.current !== currentUserId) {
            pageTurnEpochRef.current += 1;
            activeSubmitIdRef.current += 1;
            isSubmittingRef.current = false;
            setSubmitting(false);
            const nextAccountPersisted = readPersistedBookingAttempt(currentUserId);
            lastConfirmationAttemptRef.current = nextAccountPersisted;
            setUncertainAttempt(nextAccountPersisted && nextAccountPersisted.status === "uncertain" ? nextAccountPersisted : null);
            setSuccessBooking(null);
            setBookingError(null);
            setPageSpecialtyId("");
            setPageDoctorId("");
            setPageSlotId("");
            setPageReason("");
            setStep(1);
        }
        prevUserIdRef.current = currentUserId;
    }, [user]);

    // 1. Fetch Specialties
    useEffect(() => {
        const fetchSpecs = async () => {
            try {
                setLoadingSpecs(true);
                setSpecialtiesError(false);
                const res = await axiosClient.get<unknown, ApiResponse<Specialty[]>>("/specialties");
                if (res.success && res.data) {
                    setSpecialties(res.data);
                } else {
                    setSpecialties([]);
                    setSpecialtiesError(true);
                }
            } catch (err) {
                console.error("Failed to load specialties", err);
                setSpecialties([]);
                setSpecialtiesError(true);
            } finally {
                setLoadingSpecs(false);
            }
        };
        fetchSpecs();
    }, [specialtiesReloadKey]);

    // 2. Parse URL parameters and AI context safely
    useEffect(() => {
        if (specialties.length === 0) return;

        let targetSpecId: number | null = null;
        let targetDocId: number | null = null;
        let targetSlotDate = slotDate;

        if (pendingSpecialtyId) {
            targetSpecId = pendingSpecialtyId;
            setPendingSpecialtyId(null);
        } else {
            const params = new URLSearchParams(location.search);
            const querySpecId = params.get("specialtyId");
            if (querySpecId) {
                targetSpecId = parseInt(querySpecId, 10);
            }
            const queryDocId = params.get("doctorId");
            if (queryDocId) {
                targetDocId = parseInt(queryDocId, 10);
            }
            const queryDate = params.get("date");
            if (queryDate) {
                if (isSundayDateString(queryDate)) {
                    showAlert("Phòng khám không mở lịch khám vào Chủ nhật. Hệ thống đã tự động chuyển sang ngày làm việc gần nhất.", "Lưu ý", "info");
                    targetSlotDate = getNextWorkingDateString(new Date(queryDate));
                } else {
                    targetSlotDate = queryDate;
                }
                setPageSlotDate(targetSlotDate);
            }
        }

        if (targetSpecId && !isNaN(targetSpecId) && specialties.some(s => s.id === targetSpecId)) {
            setPageSpecialtyId(targetSpecId);
            const specialty = specialties.find(s => s.id === targetSpecId);
            setActiveDraft(previous => {
                const specialtyChanged = previous?.specialtyId !== targetSpecId;
                return {
                    ...previous,
                    specialtyId: targetSpecId,
                    specialtyName: specialty?.specialtyName,
                    doctorId: specialtyChanged ? undefined : previous?.doctorId,
                    doctorName: specialtyChanged ? undefined : previous?.doctorName,
                    slotId: specialtyChanged ? undefined : previous?.slotId,
                    startTime: specialtyChanged ? undefined : previous?.startTime,
                    endTime: specialtyChanged ? undefined : previous?.endTime,
                    slotDate: targetSlotDate,
                    reason: previous?.reason,
                    isComplete: false,
                    version: previous?.version !== undefined ? (specialtyChanged ? previous.version + 1 : previous.version) : undefined,
                    confirmationId: specialtyChanged ? undefined : previous?.confirmationId
                };
            });
        }

        if (targetDocId && !isNaN(targetDocId)) {
            setPageDoctorId(targetDocId);
            setActiveDraft(previous => ({
                ...previous,
                doctorId: targetDocId,
                slotId: undefined,
                startTime: undefined,
                endTime: undefined,
                isComplete: false,
                version: previous?.version !== undefined ? previous.version + 1 : undefined,
                confirmationId: undefined
            }));
        }
    }, [specialties, pendingSpecialtyId, location.search, setPendingSpecialtyId, setActiveDraft, showAlert, slotDate]);

    // 3. Fetch Doctors when specialtyId changes
    useEffect(() => {
        if (!specialtyId) {
            setDoctors([]);
            return;
        }

        let active = true;

        const fetchDoctors = async () => {
            try {
                setLoadingDocs(true);
                setDoctorsError(false);
                const res = await axiosClient.get<unknown, ApiResponse<Doctor[] | { items?: Doctor[] }>>(`/specialties/${specialtyId}/doctors`);
                if (!active) return;
                if (res.success && res.data) {
                    const list = Array.isArray(res.data) ? res.data : (res.data.items ?? []);
                    setDoctors(list);
                    
                    // If current doctorId is not in the specialty's doctor list, prompt and reset
                    if (doctorId && !list.some((d: Doctor) => d.id === doctorId)) {
                        setPageDoctorId("");
                        setPageSlotId("");
                        setActiveDraft(previous => previous ? {
                            ...previous,
                            doctorId: undefined,
                            doctorName: undefined,
                            slotId: undefined,
                            startTime: undefined,
                            endTime: undefined,
                            isComplete: false,
                            version: previous.version !== undefined ? previous.version + 1 : undefined,
                            confirmationId: undefined
                        } : previous);
                        showAlert("Bác sĩ đã chọn không thuộc chuyên khoa này. Vui lòng chọn lại bác sĩ.", "Thông báo", "info");
                    } else if (revisitRequestId && doctorId) {
                        setStep(3);
                    }
                } else {
                    setDoctors([]);
                    setDoctorsError(true);
                }
            } catch (err) {
                if (!active) return;
                console.error("Failed to fetch doctors", err);
                setDoctors([]);
                setDoctorsError(true);
            } finally {
                if (active) setLoadingDocs(false);
            }
        };

        fetchDoctors();
        return () => {
            active = false;
        };
    }, [specialtyId, doctorId, revisitRequestId, setActiveDraft, showAlert, doctorsReloadKey]);

    // 4. Fetch Slots when doctorId and slotDate are set
    useEffect(() => {
        if (!doctorId || !slotDate) {
            setSlots([]);
            setPageSlotId("");
            return;
        }

        if (isSundayDateString(slotDate)) {
            setSlots([]);
            setPageSlotId("");
            return;
        }

        let active = true;

        const fetchSlots = async () => {
            try {
                setLoadingSlots(true);
                setSlotsError(false);
                const specParam = specialtyId ? `&specialtyId=${specialtyId}` : '';
                const res = await axiosClient.get<unknown, ApiResponse<Slot[]>>(
                    `/doctors/${doctorId}/available-slots?fromDate=${slotDate}&toDate=${slotDate}${specParam}`
                );
                if (!active) return;
                if (res.success && res.data) {
                    setSlots(res.data);
                    if (slotId && !res.data.some(slot => slot.slotId === slotId)) {
                        setPageSlotId("");
                        setActiveDraft(previous => previous ? {
                            ...previous,
                            slotId: undefined,
                            startTime: undefined,
                            endTime: undefined,
                            isComplete: false,
                            version: previous.version !== undefined ? previous.version + 1 : undefined,
                            confirmationId: undefined
                        } : previous);
                    }
                } else {
                    setSlots([]);
                    setSlotsError(true);
                }
            } catch (err) {
                if (!active) return;
                console.error("Failed to fetch slots", err);
                setSlots([]);
                setSlotsError(true);
            } finally {
                if (active) setLoadingSlots(false);
            }
        };

        fetchSlots();
        return () => {
            active = false;
        };
    }, [doctorId, slotDate, specialtyId, slotId, setActiveDraft, slotsReloadKey]);

    // Handler when selecting specialty: discards doctor & slot
    const handleSelectSpecialty = (id: number) => {
        if (specialtyId !== id) {
            const specialty = specialties.find(item => item.id === id);
            setPageSpecialtyId(id);
            setPageDoctorId("");
            setPageSlotId("");
            setSlots([]);
            setActiveDraft(previous => ({
                specialtyId: id,
                specialtyName: specialty?.specialtyName,
                slotDate,
                reason: previous?.reason ?? reason,
                isComplete: false,
                version: previous?.version !== undefined ? previous.version + 1 : undefined,
                confirmationId: undefined
            }));
        }
    };

    // Handler when selecting doctor: discards slot
    const handleSelectDoctor = (id: number) => {
        if (doctorId !== id) {
            const doctor = doctors.find(item => item.id === id);
            setPageDoctorId(id);
            setPageSlotId("");
            setSlots([]);
            setActiveDraft(previous => ({
                ...previous,
                specialtyId: Number(specialtyId),
                specialtyName: selectedSpec?.specialtyName,
                doctorId: id,
                doctorName: doctor ? formatDoctorName(doctor.academicTitle, doctor.fullName) : undefined,
                slotId: undefined,
                startTime: undefined,
                endTime: undefined,
                slotDate,
                reason: previous?.reason ?? reason,
                isComplete: false,
                version: previous?.version !== undefined ? previous.version + 1 : undefined,
                confirmationId: undefined
            }));
        }
    };

    const handleSelectSlot = (slot: Slot) => {
        setPageSlotId(slot.slotId);
        setActiveDraft(previous => {
            const unchangedSlot = previous?.slotId === slot.slotId && previous?.slotDate === slot.slotDate;
            return {
                ...previous,
                specialtyId: Number(specialtyId),
                specialtyName: selectedSpec?.specialtyName,
                doctorId: Number(doctorId),
                doctorName: selectedDoc ? formatDoctorName(selectedDoc.academicTitle, selectedDoc.fullName) : undefined,
                slotId: slot.slotId,
                slotDate: slot.slotDate,
                startTime: slot.startTime.substring(0, 5),
                endTime: slot.endTime.substring(0, 5),
                reason: normalizeBookingReason(reason),
                isComplete: isValidBookingReason(reason),
                version: previous?.version !== undefined ? (unchangedSlot ? previous.version : previous.version + 1) : undefined,
                confirmationId: unchangedSlot ? previous?.confirmationId : undefined
            };
        });
    };

    const handleSlotDateChange = (nextDate: string) => {
        setPageSlotDate(nextDate);
        setPageSlotId("");
        setActiveDraft(previous => previous ? {
            ...previous,
            slotDate: nextDate,
            slotId: undefined,
            startTime: undefined,
            endTime: undefined,
            isComplete: false,
            version: previous.version !== undefined ? previous.version + 1 : undefined,
            confirmationId: undefined
        } : previous);
    };

    const handleReasonChange = (nextReason: string) => {
        const normalizedReason = normalizeBookingReason(nextReason);
        setPageReason(normalizedReason);
        setActiveDraft(previous => {
            if (!previous) return previous;
            const unchangedReason = normalizeBookingReason(previous.reason) === normalizedReason;
            return {
                ...previous,
                reason: normalizedReason,
                isComplete: Boolean(
                    previous.specialtyId &&
                    previous.doctorId &&
                    previous.slotId &&
                    isValidBookingReason(normalizedReason)
                ),
                version: previous.version !== undefined ? (unchangedReason ? previous.version : previous.version + 1) : undefined,
                confirmationId: unchangedReason ? previous.confirmationId : undefined
            };
        });
    };

    const handleDiscardUncertainAndStartNewTurn = () => {
        pageTurnEpochRef.current += 1;
        activeSubmitIdRef.current += 1;
        isSubmittingRef.current = false;
        setSubmitting(false);
        lastConfirmationAttemptRef.current = null;
        setUncertainAttempt(null);
        setBookingError(null);
        removePersistedBookingAttempt(currentAccountKeyRef.current ?? "anon");
        setActiveDraft(null);
        setPageSpecialtyId("");
        setPageDoctorId("");
        setPageSlotId("");
        setPageReason("");
        setStep(1);
    };

    // Confirm booking submit
    const handleConfirm = async () => {
        if (submitting || isSubmittingRef.current || !specialtyId || !doctorId || !slotId) return;
        const normalizedReason = normalizeBookingReason(reason);
        if (!isValidBookingReason(normalizedReason)) {
            window.dispatchEvent(new CustomEvent("cliniccare:focus-booking-reason"));
            showAlert("Lý do khám phải từ 10 đến 500 ký tự và cần mô tả triệu chứng cụ thể.", "Thông báo", "error");
            return;
        }

        const submitId = ++activeSubmitIdRef.current;
        const requestAccountKey = currentAccountKeyRef.current ?? "anon";
        const requestDraftId = activeDraftRef.current?.draftId;
        const requestDraftVersion = activeDraftRef.current?.version;
        const requestTurnEpoch = pageTurnEpochRef.current;
        const requestTurnIdentity = `${requestAccountKey}_${requestDraftId ?? `epoch_${requestTurnEpoch}`}`;
        const payloadFingerprint = `${revisitRequestId ?? "std"}_${specialtyId}_${doctorId}_${slotDate}_${slotId}_${normalizedReason}`;

        const isCurrentContext = () =>
            isMountedRef.current &&
            submitId === activeSubmitIdRef.current &&
            (currentAccountKeyRef.current ?? "anon") === requestAccountKey &&
            pageTurnEpochRef.current === requestTurnEpoch &&
            activeDraftRef.current?.draftId === requestDraftId &&
            currentPayloadFingerprintRef.current === payloadFingerprint;

        isSubmittingRef.current = true;
        setSubmitting(true);
        setBookingError(null);

        let idempotencyKey: string | undefined;

        try {

            // 1. Check in-memory ref for active/uncertain attempt in the exact same turn and payload
            if (
                lastConfirmationAttemptRef.current &&
                lastConfirmationAttemptRef.current.accountKey === requestAccountKey &&
                lastConfirmationAttemptRef.current.turnIdentity === requestTurnIdentity &&
                lastConfirmationAttemptRef.current.payloadFingerprint === payloadFingerprint &&
                lastConfirmationAttemptRef.current.status !== "succeeded"
            ) {
                idempotencyKey = lastConfirmationAttemptRef.current.key;
            }

            // 2. Reconcile from sessionStorage if remounted/reloaded or started in widget while an earlier attempt in the same draft was uncertain/in-flight
            if (!idempotencyKey) {
                const persisted = readPersistedBookingAttempt(requestAccountKey);
                if (persisted && persisted.accountKey === requestAccountKey && persisted.status !== "succeeded") {
                    if (persisted.payloadFingerprint === payloadFingerprint) {
                        const isAiConfirmationFlow = Boolean(activeDraft?.confirmationId || persisted.confirmationId);
                        if (isAiConfirmationFlow) {
                            const matchesDraft = requestDraftId
                                ? persisted.draftId === requestDraftId
                                : !persisted.draftId;
                            const matchesVersion = (requestDraftVersion !== undefined && persisted.draftVersion !== undefined)
                                ? persisted.draftVersion === requestDraftVersion
                                : true;
                            const matchesConfirmation = Boolean(activeDraft?.confirmationId) && persisted.confirmationId === activeDraft?.confirmationId;
                            const matchesSession = activeDraft?.sessionId
                                ? persisted.sessionId === activeDraft.sessionId
                                : !persisted.sessionId;
                            const matchesSnapshot = activeDraft?.contextSnapshotId
                                ? persisted.contextSnapshotId === activeDraft.contextSnapshotId
                                : !persisted.contextSnapshotId;
                            if (matchesDraft && matchesVersion && matchesConfirmation && matchesSession && matchesSnapshot) {
                                idempotencyKey = persisted.key;
                            }
                        } else {
                            // Manual form attempt (no AI confirmationId): reuse K1 when retrying the exact same payload after timeout/reload
                            idempotencyKey = persisted.key;
                        }
                    } else {
                        // User modified slot/doctor/specialty/reason and submitted a different payload -> discard old uncertain attempt so K1 is never reused
                        removePersistedBookingAttempt(requestAccountKey, persisted.key);
                        setUncertainAttempt(null);
                    }
                }
            }

            if (!idempotencyKey) {
                idempotencyKey = typeof crypto !== "undefined" && crypto.randomUUID
                    ? crypto.randomUUID()
                    : `${fallbackFormId}_${submitId}`;
            }

            const attemptRecord: PendingBookingAttemptRecord = {
                accountKey: requestAccountKey,
                turnIdentity: requestTurnIdentity,
                draftId: requestDraftId,
                draftVersion: requestDraftVersion,
                confirmationId: activeDraft?.confirmationId,
                sessionId: activeDraft?.sessionId,
                contextSnapshotId: activeDraft?.contextSnapshotId,
                payloadFingerprint,
                key: idempotencyKey,
                status: "in_flight",
                isManualFormAttempt: !activeDraft?.confirmationId
            };
            lastConfirmationAttemptRef.current = attemptRecord;
            writePersistedBookingAttempt(attemptRecord);

            const appointmentPayload = {
                doctorId,
                specialtyId,
                appointmentSlotId: slotId,
                reason: normalizedReason,
                ...(activeDraft?.confirmationId ? {
                    confirmationId: activeDraft.confirmationId,
                    contextSnapshotId: activeDraft.contextSnapshotId,
                    sessionId: activeDraft.sessionId,
                    draftId: activeDraft.draftId,
                    draftVersion: activeDraft.version
                } : {})
            };

            const res = revisitRequestId
                ? await axiosClient.post<unknown, ApiResponse<AppointmentResponsePayload>>(
                    `/revisit-requests/${revisitRequestId}/accept`,
                    {
                        targetSlotId: slotId,
                        reason: normalizedReason
                    },
                    {
                        headers: {
                            "Idempotency-Key": idempotencyKey
                        }
                    }
                )
                : await axiosClient.post<unknown, ApiResponse<AppointmentResponsePayload>>(
                    "/appointments",
                    appointmentPayload,
                    {
                        headers: {
                            "Idempotency-Key": idempotencyKey
                        }
                    }
                );

            if (res.success && res.data) {
                // Clear attempt only if it still matches this request's idempotencyKey
                if (lastConfirmationAttemptRef.current?.key === idempotencyKey) {
                    lastConfirmationAttemptRef.current = null;
                    setUncertainAttempt(null);
                }
                removePersistedBookingAttempt(requestAccountKey, idempotencyKey);

                // If draft/session/user changed while Request A was in-flight, NEVER overwrite Draft B!
                if (!isCurrentContext()) {
                    if (isMountedRef.current && (currentAccountKeyRef.current ?? "anon") === requestAccountKey) {
                        showAlert(
                            `Lịch hẹn trước đó (${res.data.appointmentCode || res.data.id || ""}) đã được hệ thống ghi nhận. Bản nháp hiện tại của bạn được giữ nguyên.`,
                            "Thông báo",
                            "info"
                        );
                    }
                    return;
                }

                setSuccessBooking({
                    appointmentId: res.data.id || res.data.appointmentId || 0,
                    appointmentCode: res.data.appointmentCode,
                    doctorName: res.data.doctorName || selectedDoc?.fullName || "",
                    specialtyName: res.data.specialtyName || selectedSpec?.specialtyName || "",
                    slotDate,
                    startTime: (res.data.startTime || selectedSlot?.startTime || "").substring(0, 5),
                    endTime: (res.data.endTime || selectedSlot?.endTime || "").substring(0, 5),
                    reason: res.data.reason || normalizedReason
                });

                setActiveDraft(null);
                setPageSpecialtyId("");
                setPageDoctorId("");
                setPageSlotId("");
                setPageReason("");

                if (revisitRequestId) {
                    navigate("/patient/book", { replace: true });
                }
            }
        } catch (err: unknown) {
            const apiErr = err as {
                errorCode?: string;
                message?: string;
                response?: { status?: number; data?: { errorCode?: string; message?: string } };
            };
            const errorCode = apiErr?.response?.data?.errorCode || apiErr?.errorCode;
            const status = apiErr?.response?.status;
            const isDeterministicRejection =
                errorCode === "SLOT_ALREADY_BOOKED" ||
                errorCode === "PATIENT_TIME_CONFLICT" ||
                errorCode === "DOCTOR_NOT_AVAILABLE" ||
                status === 400 ||
                status === 409 ||
                status === 422;

            // GUARD: Do NOT mutate attempt state if context changed while request was in-flight
            if (!isCurrentContext()) {
                return;
            }

            if (isDeterministicRejection) {
                if (lastConfirmationAttemptRef.current?.key === idempotencyKey) {
                    lastConfirmationAttemptRef.current = null;
                    setUncertainAttempt(null);
                }
                removePersistedBookingAttempt(requestAccountKey, idempotencyKey);
            } else if (lastConfirmationAttemptRef.current && lastConfirmationAttemptRef.current.key === idempotencyKey) {
                // Network error / timeout: outcome on server is uncertain, preserve idempotency key for safe retry
                const uncertainRecord: PendingBookingAttemptRecord = {
                    ...lastConfirmationAttemptRef.current,
                    status: "uncertain"
                };
                lastConfirmationAttemptRef.current = uncertainRecord;
                setUncertainAttempt(uncertainRecord);
                writePersistedBookingAttempt(uncertainRecord);
            }

            let msg = apiErr?.response?.data?.message || apiErr?.message || "Có lỗi xảy ra khi đặt lịch.";
            if (errorCode === "SLOT_ALREADY_BOOKED") {
                msg = "Khung giờ này vừa được đặt bởi người khác. Vui lòng chọn giờ khác.";
                setPageSlotId("");
                setActiveDraft(previous => previous ? {
                    ...previous,
                    slotId: undefined,
                    startTime: undefined,
                    endTime: undefined,
                    isComplete: false,
                    version: (previous.version ?? 1) + 1,
                    confirmationId: undefined
                } : previous);
                setStep(3);
            } else if (errorCode === "PATIENT_TIME_CONFLICT") {
                msg = "Bạn đã có một lịch khám khác trùng vào khung giờ này.";
                setPageSlotId("");
                setActiveDraft(previous => previous ? {
                    ...previous,
                    slotId: undefined,
                    startTime: undefined,
                    endTime: undefined,
                    isComplete: false,
                    version: (previous.version ?? 1) + 1,
                    confirmationId: undefined
                } : previous);
                setStep(3);
            } else if (errorCode === "DOCTOR_NOT_AVAILABLE") {
                msg = "Bác sĩ không có ca trực hoặc nghỉ phép vào giờ này.";
                setPageSlotId("");
                setActiveDraft(previous => previous ? {
                    ...previous,
                    slotId: undefined,
                    startTime: undefined,
                    endTime: undefined,
                    isComplete: false,
                    version: (previous.version ?? 1) + 1,
                    confirmationId: undefined
                } : previous);
                setStep(3);
            }
            setBookingError(msg);
            showAlert(msg, "Thông báo đặt lịch", "error");
        } finally {
            if (submitId === activeSubmitIdRef.current && isMountedRef.current) {
                isSubmittingRef.current = false;
                setSubmitting(false);
            }
        }
    };

    const selectedSpec = specialties.find(s => s.id === specialtyId);
    const selectedDoc = doctors.find(d => d.id === doctorId);
    const selectedSlot = slots.find(s => s.slotId === slotId);
    const displayStep = step === 1 && activeDraft?.slotId ? 3 : step;

    // Success Screen
    if (successBooking) {
        return (
            <div className={styles.pageWrapper}>
                <Card className={styles.successPanel}>
                    <Result status="success" title="Đặt lịch khám thành công!" subTitle="Lịch hẹn của bạn đã được ghi nhận vào hệ thống phòng khám ClinicCare." />
                    <Descriptions bordered column={1} items={[
                        { key: 'code', label: 'Mã lịch hẹn:', children: <strong>#{successBooking.appointmentCode}</strong> },
                        { key: 'specialty', label: 'Chuyên khoa:', children: successBooking.specialtyName },
                        { key: 'doctor', label: 'Bác sĩ khám:', children: successBooking.doctorName },
                        { key: 'time', label: 'Thời gian hẹn:', children: <strong>{successBooking.startTime} - {successBooking.endTime} ({successBooking.slotDate})</strong> },
                        { key: 'reason', label: 'Lý do khám:', children: successBooking.reason ? `"${successBooking.reason}"` : 'Không có' }
                    ]} />
                    <Alert className={styles.notice} type="info" showIcon title={<><strong>Lưu ý:</strong> Vui lòng có mặt tại quầy lễ tân trước giờ hẹn 10-15 phút và cung cấp mã lịch hẹn <strong>#{successBooking.appointmentCode}</strong> để được tiếp đón ưu tiên.</>} />
                    <div className={styles.actions}>
                        <Button type="primary" onClick={() => navigate('/patient/appointments')}>Xem lịch hẹn của tôi</Button>
                        <Button onClick={() => {
                            setSuccessBooking(null);
                            setStep(1);
                            setPageSpecialtyId("");
                            setPageDoctorId("");
                            setPageSlotId("");
                            setPageReason("");
                            setActiveDraft(null);
                        }}>Đặt thêm lịch hẹn khác</Button>
                    </div>
                </Card>
            </div>
        );
    }

    if (loadingSpecs) {
        return <div className={styles.pageWrapper}><Card><div className={styles.loadingPanel}><Spin /><span>Đang tải dữ liệu phòng khám...</span></div></Card></div>;
    }

    return (
        <div className={styles.pageWrapper}>
            <Breadcrumb items={[{ label: 'Trang chủ', path: '/' }, { label: 'Đặt lịch khám' }]} />
            {revisitRequestId && <Alert className={styles.notice} role="status" type="info" showIcon title="Bạn đang chọn khung giờ cho một đề xuất tái khám. Lịch mới chỉ được tạo sau khi xác nhận ở bước cuối." />}
            <div className={styles.pageHeader}>
                <h1 className={styles.pageTitle}>Đặt lịch khám Chuyên khoa</h1>
                <p className={styles.pageSubtitle}>Chọn chuyên khoa, bác sĩ và khung giờ 30 phút thuận tiện để được tiếp đón chu đáo không phải chờ đợi.</p>
            </div>
            <div className={styles.stepperContainer} role="progressbar" aria-valuenow={displayStep} aria-valuemin={1} aria-valuemax={4}>
                <Steps current={displayStep - 1} items={[
                    { title: '1. Chuyên khoa' }, { title: '2. Bác sĩ' }, { title: '3. Thời gian' }, { title: '4. Xác nhận' }
                ]} />
            </div>
            <div className={styles.bookingLayout}>
                <Card className={styles.mainCard}>
                    {specialtiesError && displayStep > 1 && <Alert className={styles.notice} type="warning" showIcon title="Không tải được danh sách chuyên khoa." action={<Button onClick={() => setSpecialtiesReloadKey(key => key + 1)}>Thử lại</Button>} />}
                    {uncertainAttempt && <Alert className={styles.notice} role="status" type="warning" showIcon
                        title="Lượt đặt lịch trước đó đang chưa rõ kết quả do gián đoạn kết nối"
                        description={<>
                            <p>Nếu bạn giữ/chọn lại đúng thông tin cũ và bấm Xác nhận, hệ thống sẽ tự động gửi lại mã đối soát cũ để nhận kết quả mà không tạo lịch thứ hai. Hoặc bạn có thể kiểm tra danh sách lịch hẹn đã tạo trước khi bắt đầu lượt mới.</p>
                            <div className={styles.actions}>
                                <Button onClick={() => navigate('/patient/appointments')}>Đối soát lịch hẹn đã tạo</Button>
                                <Button onClick={handleDiscardUncertainAndStartNewTurn}>Hủy & bắt đầu lượt đặt lịch mới</Button>
                            </div>
                        </>} />}
                    {bookingError && <Alert className={styles.notice} type="error" showIcon title={bookingError} />}
                    {displayStep === 1 && <div>
                        <h2 className={styles.stepTitle}>Bước 1: Chọn chuyên khoa khám</h2>
                        <p className={styles.stepSubtitle}>Vui lòng chọn một chuyên khoa phù hợp với tình trạng sức khỏe hiện tại của bạn.</p>
                        {specialtiesError ? <Alert type="error" showIcon title="Không tải được danh sách chuyên khoa." action={<Button onClick={() => setSpecialtiesReloadKey(key => key + 1)}>Thử lại</Button>} /> : specialties.length === 0 ? <Empty description="Chưa có chuyên khoa để đặt lịch." /> : <div className={styles.selectionGrid}>
                            {specialties.map(s => <Card key={s.id} hoverable role="button" tabIndex={0} aria-pressed={specialtyId === s.id}
                                className={`${styles.selectionCard} ${specialtyId === s.id ? styles.selectionCardActive : ''}`}
                                onClick={() => handleSelectSpecialty(s.id)}
                                onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); handleSelectSpecialty(s.id); } }}>
                                <Stethoscope size={30} className={styles.cardIcon} />
                                <h4>{s.specialtyName}</h4><p>{s.description || 'Khám và điều trị chuyên sâu'}</p>
                                {s.aiEnabled && <Tag className={styles.aiTag} icon={<Sparkles size={12} />}>Hỗ trợ AI</Tag>}
                            </Card>)}
                        </div>}
                        <div className={styles.stepFooter}>
                            <span className={styles.consultationHint}>Chưa biết nên chọn khoa nào? <Link to="/patient/ai-consultation" className={styles.consultationLink}>Tư vấn cùng AI &rarr;</Link></span>
                            <Button type="primary" size="large" onClick={() => setStep(2)} disabled={!specialtyId}>Tiếp tục: Chọn bác sĩ <ArrowRight size={18} /></Button>
                        </div>
                    </div>}
                    {displayStep === 2 && <div>
                        <h2 className={styles.stepTitle}>Bước 2: Chọn bác sĩ ({selectedSpec?.specialtyName})</h2>
                        <p className={styles.stepSubtitle}>Chọn bác sĩ mà bạn muốn trực tiếp thăm khám và tư vấn.</p>
                        {loadingDocs ? <div className={styles.loadingPanel}><Spin /><span>Đang tải danh sách bác sĩ thuộc chuyên khoa...</span></div>
                            : doctorsError ? <Alert type="error" showIcon title="Không tải được danh sách bác sĩ." action={<Button onClick={() => setDoctorsReloadKey(key => key + 1)}>Thử lại</Button>} />
                            : doctors.length === 0 ? <Empty description={<><p>Chưa có bác sĩ thuộc chuyên khoa này</p><p>Vui lòng quay lại chọn chuyên khoa khác hoặc liên hệ hotline phòng khám.</p></>}>
                                <Button onClick={() => setStep(1)}><ArrowLeft size={16} /> Chọn lại chuyên khoa</Button>
                            </Empty> : <div className={styles.selectionGrid}>
                                {doctors.map(d => <Card key={d.id} hoverable role="button" tabIndex={0} aria-pressed={doctorId === d.id}
                                    className={`${styles.selectionCard} ${doctorId === d.id ? styles.selectionCardActive : ''}`}
                                    onClick={() => handleSelectDoctor(d.id)}
                                    onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); handleSelectDoctor(d.id); } }}>
                                    <Avatar size={48} icon={<User size={26} />} className={styles.doctorAvatar} />
                                    <h4>{formatDoctorName(d.academicTitle, d.fullName)}</h4><p>{d.experienceYears ? `${d.experienceYears} năm kinh nghiệm` : 'Chưa cập nhật kinh nghiệm'}</p>
                                </Card>)}
                            </div>}
                        <div className={styles.stepFooter}>
                            <Button onClick={() => setStep(1)}><ArrowLeft size={16} /> Quay lại</Button>
                            <Button type="primary" size="large" onClick={() => setStep(3)} disabled={!doctorId}>Tiếp tục: Chọn giờ khám <ArrowRight size={18} /></Button>
                        </div>
                    </div>}
                    {displayStep === 3 && <div>
                        <h2 className={styles.stepTitle}>Bước 3: Chọn ngày & giờ khám</h2>
                        <p className={styles.stepSubtitle}>Lịch trống của {formatDoctorName(selectedDoc?.academicTitle, selectedDoc?.fullName)}</p>
                        <Form layout="vertical" component="div">
                            <Form.Item className={styles.dateField} label="Chọn ngày khám" htmlFor="slotDate" required>
                                <Input type="date" id="slotDate" min={toLocalDateString()} value={slotDate} onChange={e => handleSlotDateChange(e.target.value)} />
                            </Form.Item>
                            <Form.Item label="Khung giờ khám còn trống (*)">
                                {isSundayDateString(slotDate) ? <Alert type="warning" showIcon title={<><strong>Lưu ý:</strong> Phòng khám không mở lịch bác sĩ vào Chủ nhật. Vui lòng chọn ngày từ Thứ Hai đến Thứ Bảy.</>} />
                                    : loadingSlots ? <div className={styles.loadingPanel}><Spin /><span>Đang kiểm tra lịch trống của bác sĩ...</span></div>
                                    : slotsError ? <Alert type="error" showIcon title="Không tải được khung giờ khám." action={<Button onClick={() => setSlotsReloadKey(key => key + 1)}>Thử lại</Button>} />
                                    : slots.length === 0 ? <Empty description={`Bác sĩ chưa có lịch trống trong ngày ${slotDate}. Quý khách vui lòng chọn một ngày khám khác.`} />
                                    : <div className={styles.slotGrid}>{slots.map(s => <Button key={s.slotId} className={`${styles.slotBtn} ${slotId === s.slotId ? styles.slotBtnActive : ""}`} type={slotId === s.slotId ? 'primary' : 'default'} aria-pressed={slotId === s.slotId} onClick={() => handleSelectSlot(s)}><Clock size={14} />{s.startTime.substring(0, 5)}</Button>)}</div>}
                            </Form.Item>
                            <Form.Item label="Triệu chứng hoặc lý do thăm khám (*)" htmlFor="reason" required
                                extra={`Tối thiểu 10 ký tự, tối đa 500 ký tự. (${reason.trim().length}/500 ký tự)`}
                                validateStatus={reason.trim().length > 0 && (reason.trim().length < 10 || reason.trim().length > 500) ? 'error' : undefined}
                                help={reason.trim().length > 0 && reason.trim().length < 10 ? 'Lý do khám phải có tối thiểu 10 ký tự.' : reason.trim().length > 500 ? 'Lý do khám vượt quá 500 ký tự.' : undefined}>
                                <Input.TextArea id="reason" placeholder="Ví dụ: Đau đầu kéo dài 3 ngày, ho sốt nhẹ, cần tư vấn khám định kỳ..." value={reason} onChange={e => handleReasonChange(e.target.value)} rows={3} />
                            </Form.Item>
                        </Form>
                        <div className={styles.stepFooter}>
                            <Button onClick={() => {
                                setPageSlotId("");
                                setActiveDraft(previous => previous ? { ...previous, slotId: undefined, startTime: undefined, endTime: undefined, isComplete: false } : previous);
                                setStep(2);
                            }}><ArrowLeft size={16} /> Quay lại</Button>
                            <Button type="primary" size="large" onClick={() => setStep(4)} disabled={!slotDate || !slotId || reason.trim().length < 10 || reason.trim().length > 500}>Tiếp tục: Xác nhận <ArrowRight size={18} /></Button>
                        </div>
                    </div>}
                    {displayStep === 4 && <div>
                        <h2 className={styles.stepTitle}>Bước 4: Kiểm tra & Xác nhận thông tin</h2>
                        <p className={styles.stepSubtitle}>Vui lòng kiểm tra lại toàn bộ chi tiết lịch hẹn trước khi gửi yêu cầu đặt khám.</p>
                        <Descriptions className={styles.reviewPanel} bordered column={1} items={[
                            { key: 'specialty', label: 'Chuyên khoa', children: selectedSpec?.specialtyName },
                            { key: 'doctor', label: 'Bác sĩ phụ trách', children: formatDoctorName(selectedDoc?.academicTitle, selectedDoc?.fullName) },
                            { key: 'time', label: 'Thời gian khám', children: `${selectedSlot?.startTime.substring(0, 5) ?? ''} - ${selectedSlot?.endTime.substring(0, 5) ?? ''}` },
                            { key: 'date', label: 'Ngày', children: slotDate.split('-').reverse().join('/') },
                            { key: 'reason', label: 'Triệu chứng/Ghi chú', children: reason ? `"${reason}"` : 'Không có' }
                        ]} />
                        <Alert className={styles.notice} type="success" showIcon title="Phòng khám cam kết giữ đúng lịch hẹn và bảo mật thông tin hồ sơ y tế." />
                        <div className={styles.stepFooter}>
                            <Button onClick={() => setStep(3)} disabled={submitting}><ArrowLeft size={16} /> Quay lại</Button>
                            <Button type="primary" size="large" onClick={handleConfirm} disabled={submitting}>{submitting ? 'Đang đặt lịch...' : 'Xác nhận & Đặt lịch'}</Button>
                        </div>
                    </div>}
                </Card>
                <aside>
                    <Card className={styles.summaryCard} title={<span className={styles.summaryTitle}><FileText size={18} /> Tóm tắt lịch khám</span>}>
                        <div className={styles.summaryList}>
                            <div className={styles.summaryItem}>
                                <div className={styles.summaryItemIcon}><Stethoscope size={18} /></div>
                                <div><div className={styles.summaryItemLabel}>Chuyên khoa</div><div className={selectedSpec ? styles.summaryItemValue : styles.summaryItemEmpty}>{selectedSpec?.specialtyName || 'Chưa chọn'}</div></div>
                            </div>
                            <div className={styles.summaryItem}>
                                <div className={styles.summaryItemIcon}><User size={18} /></div>
                                <div><div className={styles.summaryItemLabel}>Bác sĩ</div><div className={selectedDoc ? styles.summaryItemValue : styles.summaryItemEmpty}>{selectedDoc ? formatDoctorName(selectedDoc.academicTitle, selectedDoc.fullName) : 'Chưa chọn'}</div></div>
                            </div>
                            <div className={styles.summaryItem}>
                                <div className={styles.summaryItemIcon}><Calendar size={18} /></div>
                                <div><div className={styles.summaryItemLabel}>Ngày khám</div><div className={slotDate ? styles.summaryItemValue : styles.summaryItemEmpty}>{slotDate ? slotDate.split('-').reverse().join('/') : 'Chưa chọn'}</div></div>
                            </div>
                            <div className={styles.summaryItem}>
                                <div className={styles.summaryItemIcon}><Clock size={18} /></div>
                                <div><div className={styles.summaryItemLabel}>Khung giờ (30 phút)</div><div className={selectedSlot ? styles.summaryItemValue : styles.summaryItemEmpty}>{selectedSlot ? `${selectedSlot.startTime.substring(0, 5)} - ${selectedSlot.endTime.substring(0, 5)}` : 'Chưa chọn'}</div></div>
                            </div>
                        </div>
                    </Card>
                </aside>
            </div>
        </div>
    );
};
