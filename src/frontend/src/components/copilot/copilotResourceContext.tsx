import { createContext, useCallback, useContext, useMemo, useState, type FC, type ReactNode } from 'react';
import type { AiCopilotResourceContext } from '../../api/aiCopilotApi';

export interface CopilotResourceSelection {
    context: AiCopilotResourceContext;
    source: string;
    label?: string;
    resourceVersion?: string | null;
    actionArguments?: Record<string, unknown>;
}

interface CopilotResourceContextValue {
    selection: CopilotResourceSelection | null;
    setSelection: (selection: CopilotResourceSelection | null) => void;
}

const defaultValue: CopilotResourceContextValue = {
    selection: null,
    setSelection: () => undefined
};

const CopilotResourceContext = createContext<CopilotResourceContextValue>(defaultValue);

export const CopilotResourceProvider: FC<{ children: ReactNode }> = ({ children }) => {
    const [selection, setSelectionState] = useState<CopilotResourceSelection | null>(null);
    const setSelection = useCallback((next: CopilotResourceSelection | null) => {
        setSelectionState(next);
    }, []);
    const value = useMemo(() => ({ selection, setSelection }), [selection, setSelection]);

    return <CopilotResourceContext.Provider value={value}>{children}</CopilotResourceContext.Provider>;
};

export const useCopilotResource = (): CopilotResourceContextValue => useContext(CopilotResourceContext);
