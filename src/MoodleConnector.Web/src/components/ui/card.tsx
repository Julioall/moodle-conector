import * as React from 'react';
export const Card = React.forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(({ className = '', ...p }, ref) => <div ref={ref} className={`ui-card ${className}`} {...p} />); Card.displayName = 'Card';
export const CardHeader = ({ className = '', ...p }: React.HTMLAttributes<HTMLDivElement>) => <div className={`ui-card-header ${className}`} {...p} />;
export const CardTitle = ({ className = '', ...p }: React.HTMLAttributes<HTMLHeadingElement>) => <h3 className={`ui-card-title ${className}`} {...p} />;
export const CardContent = ({ className = '', ...p }: React.HTMLAttributes<HTMLDivElement>) => <div className={`ui-card-content ${className}`} {...p} />;
export const CardFooter = ({ className = '', ...p }: React.HTMLAttributes<HTMLDivElement>) => <div className={`ui-card-footer ${className}`} {...p} />;
