import { Badge } from '../../../components/ui/badge';

export function EnrollmentBadge({ status }: { status: string }) {
  const label = ({ ativo: 'Ativo', suspenso: 'Suspenso', concluido: 'Concluído' } as Record<string, string>)[status] ?? status;
  return <Badge>{label}</Badge>;
}
