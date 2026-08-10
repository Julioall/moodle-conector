import * as React from 'react';
export type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'default' | 'outline' | 'ghost'; size?: 'default' | 'sm' };
export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(({ className = '', variant = 'default', size = 'default', ...p }, ref) => <button ref={ref} className={`ui-button ui-button-${variant} ui-button-${size} ${className}`} {...p} />); Button.displayName = 'Button';
