import * as React from 'react';
export const Alert = ({ className = '', ...p }: React.HTMLAttributes<HTMLDivElement>) => <div role="alert" className={`ui-alert ${className}`} {...p} />;
