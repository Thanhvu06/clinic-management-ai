import { Link, useInRouterContext } from 'react-router-dom';
import type { LinkProps } from 'react-router-dom';

// The catalog cards can also be embedded outside the application's router.
export function MedicineLink({ to, state, ...props }: LinkProps & { to: string }) {
    const inRouter = useInRouterContext();
    return inRouter ? <Link to={to} state={state} {...props} /> : <a href={to} {...props} />;
}
