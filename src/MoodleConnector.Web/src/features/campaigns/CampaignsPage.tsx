import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Clock3, Megaphone, Play, Plus, ShieldCheck } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Textarea } from '@/components/ui/textarea';
import { useSession } from '@/features/auth/useSession';
import { useConnectionScope } from '@/features/connections/useConnectionScope';
import { coursesGateway } from '@/features/courses/courses-gateway';
import { createAutomationsGateway, type AutomationInput } from '@/features/automations/automations-gateway';

const campaignsGateway = createAutomationsGateway();

function formatDate(value?: string) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString('pt-BR');
}

const conditionLabels: Record<string, string> = { overdue_submissions: 'Entregas vencidas', awaiting_grading: 'Aguardando correção', weekly_signals: 'Sinais semanais' };

export function CampaignsPage() {
  const { can } = useSession();
  const canManage = can('automations.manage');
  const { connectionRef, selectedConnection } = useConnectionScope();
  const client = useQueryClient();
  const [open, setOpen] = useState(false);
  const [courseId, setCourseId] = useState('');
  const [name, setName] = useState('');
  const [messageText, setMessageText] = useState('');
  const [conditionType, setConditionType] = useState('overdue_submissions');
  const [scheduleType, setScheduleType] = useState<'manual' | 'daily' | 'weekly'>('manual');

  const courses = useQuery({ queryKey: ['app', 'campaigns', 'courses', connectionRef], queryFn: () => coursesGateway.list(connectionRef, 1, 100), enabled: Boolean(connectionRef), staleTime: 60_000 });
  const automations = useQuery({ queryKey: ['app', 'campaigns', connectionRef], queryFn: campaignsGateway.list, staleTime: 20_000 });
  const campaigns = useMemo(() => (automations.data?.data ?? []).filter((item) => ['prepare_moodle_message', 'create_followup_and_prepare_message'].includes(item.actionType) && (!connectionRef || item.connectionAlias === connectionRef)), [automations.data?.data, connectionRef]);
  const save = useMutation({
    mutationFn: (input: AutomationInput) => campaignsGateway.create(input),
    onSuccess: () => { setOpen(false); setCourseId(''); setName(''); setMessageText(''); void client.invalidateQueries({ queryKey: ['app', 'campaigns'] }); },
  });
  const run = useMutation({ mutationFn: (id: string) => campaignsGateway.run(id), onSuccess: () => void client.invalidateQueries({ queryKey: ['app', 'campaigns'] }) });

  function createCampaign() {
    if (!connectionRef || !courseId.trim() || !name.trim() || !messageText.trim()) return;
    save.mutate({ connectionAlias: connectionRef, courseId: courseId.trim(), name: name.trim(), description: 'Campanha Moodle-first: prepara mensagem para aprovação humana.', scheduleType, runHourUtc: 11, runMinuteUtc: 0, runDayOfWeek: scheduleType === 'weekly' ? 5 : undefined, conditionType, actionType: 'prepare_moodle_message', config: { messageText: messageText.trim() }, isEnabled: scheduleType !== 'manual' });
  }

  return <main className="content-frame" aria-labelledby="campaigns-title"><header className="page-heading"><div><p className="eyebrow">OPERACIONAL · MOODLE-FIRST</p><h1 id="campaigns-title" className="flex items-center gap-2"><Megaphone className="h-6 w-6 text-primary" />Campanhas</h1><p>Transforme sinais do Moodle em mensagens preparadas para aprovação, com histórico de execução.</p></div><Button type="button" onClick={() => setOpen(true)} disabled={!canManage || !connectionRef}><Plus />Nova campanha</Button></header>
    <Card className="border-primary/20 bg-primary/[0.03]"><CardContent className="flex items-start gap-3 p-4"><ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-primary" /><div><p className="font-medium">Canal atual: mensagens Moodle</p><p className="mt-1 text-sm text-muted-foreground">Cada execução identifica destinatários a partir dos dados acadêmicos e cria uma ação pendente. Nada é enviado sem revisão e confirmação humana.</p></div></CardContent></Card>
    {!connectionRef && <Card><CardContent className="p-8 text-sm text-muted-foreground">Configure uma conexão Moodle para criar campanhas.</CardContent></Card>}
    {connectionRef && <section className="space-y-4"><div className="flex items-end justify-between gap-3"><div><h2 className="text-lg font-semibold">Campanhas configuradas</h2><p className="text-sm text-muted-foreground">Escopo: {selectedConnection?.alias ?? connectionRef}</p></div><Badge variant="outline">{campaigns.length} campanha{campaigns.length === 1 ? '' : 's'}</Badge></div>{automations.isPending ? <Card><CardContent className="p-8 text-sm text-muted-foreground">Carregando campanhas…</CardContent></Card> : campaigns.length === 0 ? <Card><CardContent className="flex flex-col items-center gap-3 p-12 text-center"><Megaphone className="h-10 w-10 text-muted-foreground/40" /><p className="font-medium">Nenhuma campanha Moodle criada</p><p className="text-sm text-muted-foreground">Comece com uma mensagem para estudantes sem entrega ou com correção pendente.</p></CardContent></Card> : <div className="grid gap-4 md:grid-cols-2">{campaigns.map((campaign) => <Card key={campaign.id}><CardHeader className="pb-3"><div className="flex items-start justify-between gap-3"><div><CardTitle className="text-base">{campaign.name}</CardTitle><CardDescription className="mt-1">Curso {campaign.courseId}</CardDescription></div><Badge variant={campaign.isEnabled ? 'default' : 'outline'}>{campaign.isEnabled ? 'Ativa' : 'Manual'}</Badge></div></CardHeader><CardContent className="space-y-3"><p className="line-clamp-3 text-sm text-muted-foreground">{campaign.config.messageText ?? 'Mensagem configurada'}</p><div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground"><Badge variant="outline">{conditionLabels[campaign.conditionType] ?? campaign.conditionType}</Badge>{campaign.scheduleType !== 'manual' && <span className="inline-flex items-center gap-1"><Clock3 className="h-3.5 w-3.5" />Próxima: {formatDate(campaign.nextRunAt)}</span>}</div><div className="flex items-center justify-between border-t pt-3 text-xs text-muted-foreground"><span>Última execução: {formatDate(campaign.lastRunAt)}</span><Button type="button" size="sm" variant="outline" onClick={() => run.mutate(campaign.id)} disabled={!canManage || run.isPending}><Play className="mr-1 h-3.5 w-3.5" />Executar e preparar</Button></div></CardContent></Card>)}</div>}</section>}
    <Dialog open={open} onOpenChange={setOpen}><DialogContent><DialogHeader><DialogTitle>Nova campanha Moodle</DialogTitle><DialogDescription>A campanha prepara uma ação de mensagem; o envio dependerá da revisão e aprovação no fluxo de mensagens.</DialogDescription></DialogHeader><div className="grid gap-4"><label className="grid gap-1.5 text-sm font-medium">Curso<select className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm" value={courseId} onChange={(event) => setCourseId(event.target.value)}><option value="">Selecione um curso</option>{(courses.data?.data ?? []).map((course) => <option key={course.courseId} value={course.courseId}>{course.displayName ?? course.fullName}</option>)}</select></label><label className="grid gap-1.5 text-sm font-medium">Nome da campanha<Input value={name} onChange={(event) => setName(event.target.value)} placeholder="Ex.: Lembrete de entrega" /></label><label className="grid gap-1.5 text-sm font-medium">Sinal do Moodle<Select value={conditionType} onValueChange={setConditionType}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="overdue_submissions">Entregas vencidas</SelectItem><SelectItem value="awaiting_grading">Aguardando correção</SelectItem><SelectItem value="weekly_signals">Sinais semanais</SelectItem></SelectContent></Select></label><label className="grid gap-1.5 text-sm font-medium">Execução<Select value={scheduleType} onValueChange={(value) => setScheduleType(value as typeof scheduleType)}><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="manual">Somente manual</SelectItem><SelectItem value="daily">Diária · 08:00 UTC</SelectItem><SelectItem value="weekly">Semanal · sexta-feira</SelectItem></SelectContent></Select></label><label className="grid gap-1.5 text-sm font-medium">Mensagem preparada<Textarea value={messageText} onChange={(event) => setMessageText(event.target.value)} placeholder="Escreva a mensagem que ficará pronta para aprovação." /></label></div><DialogFooter><Button type="button" variant="outline" onClick={() => setOpen(false)}>Cancelar</Button><Button type="button" onClick={createCampaign} disabled={save.isPending || !courseId.trim() || !name.trim() || !messageText.trim()}>{save.isPending ? 'Salvando…' : 'Criar campanha'}</Button></DialogFooter></DialogContent></Dialog>
  </main>;
}
