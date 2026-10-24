import type { ReactNode } from 'react';
import { ConfigProvider } from 'antd';
import styles from './ClinicalEditor.module.css';

export function ClinicalEditor({ readOnly, children }: { readOnly: boolean; children: ReactNode }) {
    return <ConfigProvider componentDisabled={readOnly}>
        <fieldset disabled={readOnly} className={styles.editor} aria-label="Nội dung hồ sơ khám">
            {children}
        </fieldset>
    </ConfigProvider>;
}
