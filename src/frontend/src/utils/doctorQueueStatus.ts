export const isConsultationInProgress = (status: string): boolean =>
    ['InConsultation', 'WaitingForDiagnostics', 'InDiagnostics', 'ResultsReady'].includes(status);
