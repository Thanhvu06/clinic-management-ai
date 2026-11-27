import { readFileSync } from 'node:fs';
import { render, screen } from '@testing-library/react';
import { expect, it } from 'vitest';
import { AppointmentStatusBadge } from '../components/common/AppointmentStatusBadge';
import badgeStyles from '../components/common/AppointmentStatusBadge.module.css';
import { tokenCssVariables } from '../theme/tokens';
import { appointmentStatusLabels } from '../utils/appointmentStatusLabels';

it('assigns a different background color to every appointment status', () => {
    const stylesheet = new CSSStyleSheet();
    stylesheet.replaceSync(readFileSync('src/components/common/AppointmentStatusBadge.module.css', 'utf8'));
    const rules = Array.from(stylesheet.cssRules) as CSSStyleRule[];
    const statuses = Object.keys(appointmentStatusLabels).filter(status => status !== 'InProgress');
    const { container } = render(statuses.map(status => <AppointmentStatusBadge key={status} status={status} />));
    const badges = Array.from(container.querySelectorAll('.ant-tag'));
    expect(badges).toHaveLength(statuses.length);

    const backgrounds = badges.map(badge => {
        const rule = rules.find(({ selectorText }) => {
            const className = selectorText.split('.').at(-1) ?? '';
            return badge.classList.contains(badgeStyles[className]);
        });
        const token = rule?.style.backgroundColor.match(/^var\((--cc-[\w-]+)\)$/)?.[1] ?? '';
        expect(tokenCssVariables[token]).toBeDefined();
        return tokenCssVariables[token];
    });

    expect(new Set(backgrounds).size).toBe(statuses.length);
});

it('preserves the visit queue label and generic color for a visit-only status', () => {
    render(<AppointmentStatusBadge status="WaitingForDoctor" label="Chờ bác sĩ" />);
    expect(screen.getByText('Chờ bác sĩ')).toHaveClass('ant-tag-warning');
});
