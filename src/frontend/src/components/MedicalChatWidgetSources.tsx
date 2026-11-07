import { Typography } from 'antd';
import type { ChatMessage, KnowledgeSource } from '../types/ai';

const isSource = (value: unknown): value is KnowledgeSource => {
    if (!value || typeof value !== 'object') return false;
    const source = value as Record<string, unknown>;
    return typeof source.docId === 'string' && typeof source.title === 'string' && typeof source.heading === 'string';
};

export const MedicalChatWidgetSources = ({ message }: { message: ChatMessage }) => {
    const transported = message.toolResults?.find(result => result.resultType === 'knowledge_sources')?.data?.sources;
    const sources = message.sources ?? (Array.isArray(transported) ? transported.filter(isSource) : []);
    if (message.role !== 'model' || sources.length === 0) return null;
    return <div><Typography.Text type="secondary">Nguồn: {sources.map(source => `${source.title} – ${source.heading}`).join('; ')}</Typography.Text></div>;
};
