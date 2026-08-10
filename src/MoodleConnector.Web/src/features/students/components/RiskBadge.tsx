import { Badge } from '../../../components/ui/badge';
export function RiskBadge({ level }: { level: string }) { const label = ({ normal: 'Normal', atencao: 'Atenção', risco: 'Risco', critico: 'Crítico' } as Record<string,string>)[level] ?? level; return <Badge aria-label={`Risco: ${label}`}>{label}</Badge>; }
