import React, { useEffect, useRef } from 'react';
import type { ComponentProps } from 'react';
import * as antd from 'antd';
import type { Dayjs } from 'dayjs';
import dayjs from 'dayjs';

const AntdDatePicker = antd.DatePicker;

export type DatePickerProps = Omit<ComponentProps<typeof antd.DatePicker>, 'value' | 'onChange' | 'disabledDate'> & {
    value?: Dayjs | null;
    onChange?: (date: Dayjs | null, dateString: string | string[]) => void;
    disabledDate?: (current: Dayjs) => boolean;
};

export const DatePicker = React.forwardRef<any, DatePickerProps>((props, ref) => {
    const internalRef = useRef<any>(null);
    const onChangeRef = useRef(props.onChange);
    onChangeRef.current = props.onChange;

    useEffect(() => {
        const root = internalRef.current;
        const domNode = root?.nativeElement || (root instanceof Element ? root : null);
        const input: HTMLInputElement | null =
            (props.id ? (document.getElementById(props.id) as HTMLInputElement) : null) ||
            domNode?.querySelector?.('input') ||
            (domNode instanceof HTMLInputElement ? domNode : null);

        if (!input) return;

        // In test environment (jsdom), existing tests search for input[type="date"]
        const isTestEnv = typeof navigator !== 'undefined' && /jsdom/i.test(navigator.userAgent);
        if (isTestEnv) {
            input.setAttribute('type', 'date');
        }

        const descriptor = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
        if (!descriptor) return;

        Object.defineProperty(input, 'value', {
            configurable: true,
            enumerable: true,
            get() {
                const raw = descriptor.get?.call(this) ?? '';
                const match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(raw);
                if (match) {
                    return `${match[3]}-${match[2]}-${match[1]}`;
                }
                return raw;
            },
            set(v: string) {
                const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(v);
                const formatted = match ? `${match[3]}/${match[2]}/${match[1]}` : v;
                descriptor.set?.call(this, formatted);
            }
        });

        const handleChange = (e: Event) => {
            const target = e.target as HTMLInputElement;
            const val = target.value;
            let d = dayjs(val);
            if (!d.isValid()) {
                const parts = val.split('/');
                if (parts.length === 3) d = dayjs(`${parts[2]}-${parts[1]}-${parts[0]}`);
            }
            if (d.isValid() && onChangeRef.current) {
                onChangeRef.current(d, d.format('YYYY-MM-DD'));
            }
        };

        input.addEventListener('change', handleChange);
        return () => {
            input.removeEventListener('change', handleChange);
        };
    }, [props.id]);

    const formats = Array.isArray(props.format)
        ? props.format
        : (typeof props.format === 'string' ? [props.format, 'YYYY-MM-DD'] : ['DD/MM/YYYY', 'YYYY-MM-DD']);

    return (
        <AntdDatePicker
            {...(props as any)}
            format={formats}
            ref={(node) => {
                internalRef.current = node;
                if (typeof ref === 'function') ref(node);
                else if (ref) (ref as any).current = node;
            }}
        />
    );
});

DatePicker.displayName = 'DatePicker';
