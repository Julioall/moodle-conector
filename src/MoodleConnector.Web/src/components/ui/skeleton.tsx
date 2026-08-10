import * as React from 'react';
export const Skeleton = ({ className = '', ...p }: React.HTMLAttributes<HTMLDivElement>) => <div aria-hidden="true" className={`ui-skeleton ${className}`} {...p} />;
