import React from "react";

interface SafeMarkdownProps {
    content: string;
    className?: string;
}

const SAFE_URL_PATTERN = /^(https?:\/\/|\/|tel:|mailto:)/i;

/**
 * Parses inline markdown tokens: `code`, **bold**, *italic*, [link](url).
 * Renders standard React elements without dangerous HTML injection.
 */
function parseInline(text: string): React.ReactNode[] {
    const nodes: React.ReactNode[] = [];
    let remaining = text;
    let keyIdx = 0;

    while (remaining.length > 0) {
        // Look for next token: `code`, **bold**, *italic*, [text](url)
        // Code
        const codeMatch = remaining.match(/^`([^`]+)`/);
        if (codeMatch) {
            nodes.push(<code key={keyIdx++}>{codeMatch[1]}</code>);
            remaining = remaining.slice(codeMatch[0].length);
            continue;
        }

        // Bold (**text** or __text__)
        const boldMatch = remaining.match(/^(\*\*|__)(.+?)\1/);
        if (boldMatch) {
            nodes.push(
                <strong key={keyIdx++}>
                    {parseInline(boldMatch[2])}
                </strong>
            );
            remaining = remaining.slice(boldMatch[0].length);
            continue;
        }

        // Italic (*text* or _text_) - ensure not part of a bullet or double asterisk
        const italicMatch = remaining.match(/^(\*|_)([^*_]+?)\1/);
        if (italicMatch) {
            nodes.push(
                <em key={keyIdx++}>
                    {parseInline(italicMatch[2])}
                </em>
            );
            remaining = remaining.slice(italicMatch[0].length);
            continue;
        }

        // Link: [text](url)
        const linkMatch = remaining.match(/^\[([^\]]+)\]\(([^)]+)\)/);
        if (linkMatch) {
            const label = linkMatch[1];
            const rawUrl = linkMatch[2].trim();
            const isSafe = SAFE_URL_PATTERN.test(rawUrl);

            if (isSafe) {
                const isExternal = rawUrl.startsWith("http://") || rawUrl.startsWith("https://");
                nodes.push(
                    <a
                        key={keyIdx++}
                        href={rawUrl}
                        target={isExternal ? "_blank" : undefined}
                        rel={isExternal ? "noopener noreferrer" : undefined}
                        style={{ color: "#0d9488", textDecoration: "underline", wordBreak: "break-word" }}
                    >
                        {label}
                    </a>
                );
            } else {
                nodes.push(label);
            }
            remaining = remaining.slice(linkMatch[0].length);
            continue;
        }

        // Next closest delimiter
        const nextSpecial = remaining.search(/[`*_\[]/);
        if (nextSpecial === -1) {
            nodes.push(remaining);
            break;
        } else if (nextSpecial === 0) {
            // Char matched a delimiter trigger but didn't form a valid markdown token, consume 1 char
            nodes.push(remaining[0]);
            remaining = remaining.slice(1);
        } else {
            nodes.push(remaining.slice(0, nextSpecial));
            remaining = remaining.slice(nextSpecial);
        }
    }

    return nodes;
}

export const SafeMarkdown: React.FC<SafeMarkdownProps> = ({ content, className }) => {
    if (!content) return null;

    // Split content by lines
    const lines = content.split(/\r?\n/);
    const elements: React.ReactNode[] = [];
    let currentList: string[] = [];

    const flushList = () => {
        if (currentList.length > 0) {
            const listItems = currentList.map((item, idx) => (
                <li key={idx} style={{ marginBottom: "2px" }}>
                    {parseInline(item)}
                </li>
            ));
            elements.push(
                <ul key={`ul-${elements.length}`} style={{ margin: "4px 0", paddingLeft: "20px" }}>
                    {listItems}
                </ul>
            );
            currentList = [];
        }
    };

    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const trimmed = line.trim();

        // Bullet list check: starts with "* " or "- "
        const bulletMatch = trimmed.match(/^[-*]\s+(.*)$/);
        if (bulletMatch) {
            currentList.push(bulletMatch[1]);
            continue;
        }

        // Non-list line: flush any pending list
        flushList();

        if (trimmed === "") {
            // Empty line translates to spacing between blocks
            elements.push(<div key={`br-${i}`} style={{ height: "6px" }} />);
        } else {
            elements.push(
                <p key={`p-${i}`} style={{ margin: "2px 0" }}>
                    {parseInline(trimmed)}
                </p>
            );
        }
    }

    flushList();

    return <div className={className}>{elements}</div>;
};

export default SafeMarkdown;
