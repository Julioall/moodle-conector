import * as React from 'react';
export const Badge = ({ className = '', ...p }: React.HTMLAttributes<HTMLSpanElement>) => <span className={`ui-badge ${className}`} {...p} />;
