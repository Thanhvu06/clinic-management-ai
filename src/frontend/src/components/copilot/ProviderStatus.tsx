import { providerStateLabel, providerStateTone } from './copilotConfig';
import styles from './ProviderStatus.module.css';
import './chatTokens.css';

export const ProviderStatus = ({ state = 'NotCalled', error, detail }: { state?: string; error?: string; detail?: string }) => {
    const currentState = error
        ? /quá nhiều|giới hạn|429|phản hồi quá lâu|chờ.*giây/i.test(error) || ['Degraded', 'Unavailable'].includes(state) ? 'Degraded'
            : /không thể kết nối|máy chủ.*sự cố|lỗi mạng/i.test(error) ? 'RequestError' : state
        : state;
    return <div className={styles.status} data-provider-status data-state={currentState} data-tone={providerStateTone(currentState)} title={detail}>
        <span className={styles.dot} aria-hidden="true" /><span>{providerStateLabel(currentState)}</span>
    </div>;
};
