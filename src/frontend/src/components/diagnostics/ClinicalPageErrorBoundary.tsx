import { Component, type ReactNode } from 'react';
import { Button } from 'antd';
import { InlineError } from '../common/InlineError';

export class ClinicalPageErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
    state = { failed: false };

    static getDerivedStateFromError() {
        return { failed: true };
    }

    render() {
        if (this.state.failed) return <>
            <InlineError message="Có lỗi hiển thị, vui lòng tải lại" />
            <Button onClick={() => window.location.reload()}>Tải lại</Button>
        </>;
        return this.props.children;
    }
}
