import { useEffect, useMemo, useRef, useState, type FormEvent, type KeyboardEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AlertCircle, MessageSquare, Search, Send, UserRound } from 'lucide-react';
import { format, isSameDay, isToday, isYesterday } from 'date-fns';
import { ptBR } from 'date-fns/locale';

import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/input';
import { Skeleton } from '@/components/ui/skeleton';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { useConnectionScope } from '@/features/connections/useConnectionScope';
import { getStoredMessagePreferences, subscribeToMessagePreferences, type MessagePreferences } from './message-preferences';
import { moodleMessagingGateway, type MessagePreview, type MoodleConversation, type MoodleMessage } from './moodle-messaging-gateway';
import { cn } from '@/lib/utils';
import { moodleHtmlToText, sanitizeMoodleHtml } from './moodle-html';

function formatConversationTime(value?: number | null) {
  if (!value) return '';
  const date = new Date(value * 1000);
  if (isToday(date)) return format(date, 'HH:mm', { locale: ptBR });
  if (isYesterday(date)) return 'Ontem';
  return format(date, 'dd/MM', { locale: ptBR });
}

function formatMessageDay(value: number) {
  const date = new Date(value * 1000);
  if (isToday(date)) return 'Hoje';
  if (isYesterday(date)) return 'Ontem';
  return format(date, "dd 'de' MMM", { locale: ptBR });
}

function getConversationPreview(conversation: MoodleConversation) {
  return conversation.lastMessage?.text ? moodleHtmlToText(conversation.lastMessage.text) || 'Sem mensagens' : 'Sem mensagens';
}

function ConversationListSkeleton() {
  return <div className="space-y-2" aria-label="Carregando conversas" aria-busy="true">{Array.from({ length: 6 }, (_, index) => <div key={index} className="flex items-start gap-3 rounded-2xl border px-3 py-3"><Skeleton className="h-11 w-11 shrink-0 rounded-full" /><div className="min-w-0 flex-1 space-y-2"><Skeleton className="h-3 w-3/5" /><Skeleton className="h-3 w-full" /><Skeleton className="h-3 w-4/5" /></div></div>)}</div>;
}

function MessageListSkeleton() {
  return <div className="mx-auto flex w-full max-w-3xl flex-col gap-4" aria-label="Carregando mensagens" aria-busy="true"><Skeleton className="h-3 w-20 self-center" />{[false, true, false, true].map((sent, index) => <div key={index} className={cn('flex', sent ? 'justify-end' : 'justify-start')}><div className="w-2/3 space-y-2"><Skeleton className={cn('h-4', sent ? 'ml-auto' : 'w-4/5')} /><Skeleton className={cn('h-14 rounded-3xl', sent ? 'ml-auto w-4/5' : 'w-full')} /></div></div>)}</div>;
}

function ConversationItem({ conversation, selected, onSelect }: { conversation: MoodleConversation; selected: boolean; onSelect: () => void }) {
  const preview = getConversationPreview(conversation);
  const hasUnread = conversation.unreadCount > 0;
  const timestamp = formatConversationTime(conversation.lastMessage?.createdAtUnix);

  return (
    <button type="button" onClick={onSelect} className={cn('w-full max-w-full overflow-hidden rounded-2xl border px-3 py-3 text-left transition-colors', 'hover:border-border hover:bg-muted/40', selected && 'border-primary/30 bg-primary/5 shadow-sm', hasUnread && !selected && 'border-primary/15 bg-primary/5')}>
      <div className="flex min-w-0 items-start gap-3">
        <Avatar className="h-11 w-11 shrink-0"><AvatarImage src={conversation.member.profileImageUrl ?? undefined} alt={conversation.member.fullName} /><AvatarFallback className="bg-primary/10 text-sm font-semibold text-primary">{conversation.member.fullName.charAt(0).toUpperCase() || <UserRound className="h-4 w-4" />}</AvatarFallback></Avatar>
        <div className="min-w-0 flex-1"><div className="flex items-start justify-between gap-3"><div className="min-w-0"><p className={cn('truncate text-sm', hasUnread ? 'font-semibold' : 'font-medium')}>{conversation.member.fullName}</p><p className={cn('mt-1 line-clamp-2 text-xs', hasUnread ? 'text-foreground' : 'text-muted-foreground')}>{preview}</p></div><div className="flex shrink-0 flex-col items-end gap-2">{timestamp && <span className="text-[11px] text-muted-foreground">{timestamp}</span>}{hasUnread && <span className="flex h-5 min-w-5 items-center justify-center rounded-full bg-primary px-1.5 text-[10px] font-medium text-primary-foreground">{conversation.unreadCount}</span>}</div></div></div>
      </div>
    </button>
  );
}

function ChatWindow({ conversation, messages, currentMoodleUserId, isLoading, isSending, error, sendOnEnter, onSend }: { conversation: MoodleConversation; messages: MoodleMessage[]; currentMoodleUserId?: number; isLoading: boolean; isSending: boolean; error?: string; sendOnEnter: boolean; onSend: (message: string) => Promise<void> }) {
  const [draft, setDraft] = useState('');
  const scrollRef = useRef<HTMLDivElement>(null);
  const rows = useMemo(() => messages.flatMap((message, index) => {
    const previous = messages[index - 1];
    const next = messages[index + 1];
    const messageDate = new Date(message.createdAtUnix * 1000);
    const previousDate = previous ? new Date(previous.createdAtUnix * 1000) : undefined;
    const nextDate = next ? new Date(next.createdAtUnix * 1000) : undefined;
    return [...(!previous || !isSameDay(previousDate!, messageDate) ? [{ type: 'day' as const, key: `day-${message.id}`, label: formatMessageDay(message.createdAtUnix) }] : []), { type: 'message' as const, key: message.id, message, groupedBefore: Boolean(previous && previous.senderType === message.senderType && isSameDay(previousDate!, messageDate)), groupedAfter: Boolean(next && next.senderType === message.senderType && isSameDay(messageDate, nextDate!)) }];
  }), [messages]);

  useEffect(() => { const node = scrollRef.current; if (node) node.scrollTop = node.scrollHeight; }, [messages.length, conversation.id]);

  const submit = async (event?: FormEvent) => { event?.preventDefault(); const value = draft.trim(); if (!value || isSending) return; await onSend(value); };
  const handleKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => { if (event.key === 'Enter' && !event.shiftKey && sendOnEnter) { event.preventDefault(); void submit(); } };

  return (
    <div className="flex min-h-0 min-w-0 flex-1 flex-col">
      <div className="flex shrink-0 items-center gap-3 border-b px-4 py-3"><Avatar className="h-10 w-10 shrink-0"><AvatarImage src={conversation.member.profileImageUrl ?? undefined} alt={conversation.member.fullName} /><AvatarFallback className="bg-primary/10 text-sm font-semibold text-primary">{conversation.member.fullName.charAt(0).toUpperCase()}</AvatarFallback></Avatar><div className="min-w-0"><p className="truncate text-sm font-semibold">{conversation.member.fullName}</p><p className="text-xs text-muted-foreground">{conversation.unreadCount > 0 ? `${conversation.unreadCount} nova${conversation.unreadCount > 1 ? 's' : ''}` : 'Conversa sincronizada sob demanda'}</p></div></div>
      <div ref={scrollRef} className="min-h-0 flex-1 overflow-y-auto p-4" aria-live="polite">
        {isLoading ? <MessageListSkeleton /> : messages.length === 0 ? <div className="flex h-full flex-col items-center justify-center gap-2 text-center text-muted-foreground"><MessageSquare className="h-10 w-10 opacity-40" /><p className="text-sm">Nenhuma mensagem nesta conversa</p><p className="text-xs">Envie a primeira mensagem para iniciar o contato.</p></div> : <div className="mx-auto w-full max-w-3xl">{rows.map((row) => row.type === 'day' ? <div key={row.key} className="my-4 flex items-center gap-3 text-[11px] font-medium text-muted-foreground"><div className="h-px flex-1 bg-border" /><span>{row.label}</span><div className="h-px flex-1 bg-border" /></div> : <div key={row.key} className={cn('flex', row.message.senderType === 'tutor' ? 'justify-end' : 'justify-start', row.groupedBefore ? 'mt-1.5' : 'mt-4 first:mt-0')}><div className={cn('max-w-[82%] min-w-0', row.message.senderType === 'tutor' ? 'items-end' : 'items-start')}>{!row.groupedBefore && <p className={cn('mb-1 px-1 text-[11px] font-medium text-muted-foreground', row.message.senderType === 'tutor' ? 'text-right' : 'text-left')}>{row.message.senderType === 'tutor' || row.message.senderMoodleUserId === currentMoodleUserId ? 'Você' : conversation.member.fullName}</p>}<div className={cn('border px-4 py-3 text-sm shadow-sm', row.message.senderType === 'tutor' ? 'border-primary/20 bg-primary text-primary-foreground rounded-3xl rounded-br-md' : 'border-border/70 bg-background rounded-3xl rounded-bl-md')}><div className="whitespace-pre-wrap break-words leading-6 [&_a]:underline [&_a]:underline-offset-2" dangerouslySetInnerHTML={{ __html: sanitizeMoodleHtml(row.message.text) }} />{!row.groupedAfter && <p className={cn('mt-2 text-[10px]', row.message.senderType === 'tutor' ? 'text-primary-foreground/70' : 'text-muted-foreground')}>{format(new Date(row.message.createdAtUnix * 1000), 'HH:mm', { locale: ptBR })}</p>}</div></div></div>)}</div>}
      </div>
      <form className="shrink-0 border-t p-3" onSubmit={(event) => void submit(event)}>{error && <div className="mb-2 flex items-start gap-2 rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive" role="alert"><AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />{error}</div>}<div className="flex items-end gap-2"><Textarea value={draft} onChange={(event) => setDraft(event.target.value)} onKeyDown={handleKeyDown} placeholder={sendOnEnter ? 'Escreva uma mensagem… (Enter envia)' : 'Escreva uma mensagem…'} className="min-h-11 max-h-32 resize-none" aria-label="Mensagem" /><Button type="submit" size="icon" className="h-10 w-10 shrink-0" disabled={!draft.trim() || isSending} aria-label="Enviar mensagem"><Send className="h-4 w-4" /></Button></div>{!sendOnEnter && <p className="mt-1 text-[11px] text-muted-foreground">Enter cria uma nova linha. Use o botão para enviar.</p>}</form>
    </div>
  );
}

export function MessagesPage() {
  const queryClient = useQueryClient();
  const { connectionRef } = useConnectionScope();
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedConversationId, setSelectedConversationId] = useState<number | null>(null);
  const [preferences, setPreferences] = useState<MessagePreferences>(getStoredMessagePreferences);
  const [pendingPreview, setPendingPreview] = useState<MessagePreview | null>(null);
  const [confirmationText, setConfirmationText] = useState('');
  useEffect(() => subscribeToMessagePreferences(setPreferences), []);

  const conversationsQuery = useQuery({ queryKey: ['app', 'messages', 'conversations', connectionRef], queryFn: () => moodleMessagingGateway.conversations(connectionRef), enabled: Boolean(connectionRef), staleTime: 30_000 });
  const conversations = useMemo(() => conversationsQuery.data?.data.items ?? [], [conversationsQuery.data?.data.items]);
  useEffect(() => { setSelectedConversationId((current) => current && conversations.some((item) => item.id === current) ? current : conversations[0]?.id ?? null); }, [conversations]);
  const selectedConversation = useMemo(() => conversations.find((item) => item.id === selectedConversationId) ?? null, [conversations, selectedConversationId]);
  const normalizedSearch = searchQuery.trim().toLocaleLowerCase('pt-BR');
  const filteredConversations = useMemo(() => normalizedSearch ? conversations.filter((conversation) => conversation.member.fullName.toLocaleLowerCase('pt-BR').includes(normalizedSearch) || getConversationPreview(conversation).toLocaleLowerCase('pt-BR').includes(normalizedSearch)) : conversations, [conversations, normalizedSearch]);
  const messagesQuery = useQuery({ queryKey: ['app', 'messages', 'conversation', connectionRef, selectedConversation?.member.id], queryFn: () => moodleMessagingGateway.messages(selectedConversation!.member.id, connectionRef), enabled: Boolean(connectionRef && selectedConversation), staleTime: 10_000 });
  const prepareMutation = useMutation({
    mutationFn: (message: string) => moodleMessagingGateway.prepareDirect(selectedConversation!.member.id, message, connectionRef),
    onSuccess: (response) => { setPendingPreview(response.data); setConfirmationText(''); },
  });
  const confirmMutation = useMutation({
    mutationFn: async () => {
      if (!pendingPreview?.pendingActionId) throw new Error('A prévia da mensagem expirou. Prepare a mensagem novamente.');
      return moodleMessagingGateway.confirm(pendingPreview.pendingActionId, confirmationText);
    },
    onSuccess: () => {
      const recipientId = pendingPreview?.recipients[0]?.studentId;
      if (recipientId) void queryClient.invalidateQueries({ queryKey: ['app', 'messages', 'conversation', connectionRef, Number(recipientId)] });
      void queryClient.invalidateQueries({ queryKey: ['app', 'messages', 'conversations', connectionRef] });
      setPendingPreview(null);
      setConfirmationText('');
    },
  });
  const send = async (message: string) => { await prepareMutation.mutateAsync(message); };
  const isSending = prepareMutation.isPending || confirmMutation.isPending;
  const sendMutation = { isPending: isSending, error: prepareMutation.error };

  return (
    <div className="flex h-[calc(100vh-6rem)] flex-col gap-4 animate-fade-in">
      <div className="shrink-0"><h1 className="text-2xl font-bold tracking-tight">Mensagens</h1><p className="text-muted-foreground">Canal individual do Moodle. As conversas são carregadas sob demanda pela conexão selecionada.</p></div>
      {conversationsQuery.isError && <Card className="shrink-0 border-destructive/50"><CardContent className="pt-4"><div className="flex items-start gap-3"><AlertCircle className="mt-0.5 h-5 w-5 shrink-0 text-destructive" /><div><p className="text-sm font-medium text-destructive">Erro ao carregar conversas</p><p className="mt-1 text-xs text-muted-foreground">{conversationsQuery.error instanceof Error ? conversationsQuery.error.message : 'Verifique se as funções de messaging estão habilitadas no Moodle.'}</p></div></div></CardContent></Card>}
      <div className="grid min-h-0 flex-1 grid-cols-1 gap-0 overflow-hidden rounded-2xl border lg:grid-cols-[360px_minmax(0,1fr)]">
      <div className="flex min-h-0 min-w-0 flex-col overflow-hidden border-r bg-card"><div className="shrink-0 border-b p-3"><div className="relative"><Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" /><Input type="search" placeholder="Buscar conversa…" value={searchQuery} onChange={(event) => setSearchQuery(event.target.value)} className="pl-9" aria-label="Buscar conversa" /></div><div className="mt-3 flex items-center justify-between gap-3 text-xs text-muted-foreground"><span>{conversations.length} {conversations.length === 1 ? 'conversa' : 'conversas'}</span>{conversationsQuery.isFetching && <Skeleton className="h-3 w-20" aria-label="Atualizando conversas" />}</div></div><div className="min-h-0 flex-1 overflow-y-auto p-3">{conversationsQuery.isPending ? <ConversationListSkeleton /> : filteredConversations.length === 0 ? <div className="flex flex-col items-center justify-center px-4 py-12 text-center"><MessageSquare className="mb-3 h-10 w-10 text-muted-foreground/50" /><p className="text-sm text-muted-foreground">{normalizedSearch ? 'Nenhuma conversa encontrada' : 'Nenhuma conversa no Moodle'}</p></div> : <div className="space-y-2">{filteredConversations.map((conversation) => <ConversationItem key={conversation.id} conversation={conversation} selected={selectedConversation?.id === conversation.id} onSelect={() => setSelectedConversationId(conversation.id)} />)}</div>}</div></div>
        <div className="flex min-h-0 min-w-0 flex-col bg-card">{selectedConversation ? <ChatWindow conversation={selectedConversation} messages={messagesQuery.data?.data.items ?? []} currentMoodleUserId={messagesQuery.data?.data.currentMoodleUserId} isLoading={messagesQuery.isPending} isSending={sendMutation.isPending} error={messagesQuery.error instanceof Error ? messagesQuery.error.message : sendMutation.error instanceof Error ? sendMutation.error.message : undefined} sendOnEnter={preferences.sendOnEnter} onSend={send} /> : <div className="flex flex-1 items-center justify-center"><div className="text-center"><MessageSquare className="mx-auto mb-3 h-12 w-12 text-muted-foreground/30" /><p className="text-muted-foreground">Selecione uma conversa para começar</p></div></div>}</div>
      </div>
      <Dialog open={Boolean(pendingPreview)} onOpenChange={(open) => { if (!open && !confirmMutation.isPending) { setPendingPreview(null); setConfirmationText(''); confirmMutation.reset(); } }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Revisar mensagem antes do envio</DialogTitle>
            <DialogDescription>Esta mensagem será enviada pelo canal nativo do Moodle e registrada na auditoria.</DialogDescription>
          </DialogHeader>
          {pendingPreview && <div className="grid gap-4">
            <div className="rounded-xl border bg-muted/30 p-4 text-sm">
              <p><span className="font-medium">Destinatário:</span> {pendingPreview.recipients[0]?.fullName ?? 'Conta Moodle'}</p>
              <div className="mt-3 whitespace-pre-wrap break-words leading-6 [&_a]:underline [&_a]:underline-offset-2" dangerouslySetInnerHTML={{ __html: sanitizeMoodleHtml(pendingPreview.messageText) }} />
            </div>
            <div className="rounded-xl border border-amber-500/30 bg-amber-500/5 p-3 text-xs text-muted-foreground">
              <p className="font-medium text-foreground">Confira antes de confirmar</p>
              <ul className="mt-2 list-disc space-y-1 pl-4">{pendingPreview.risks.map((risk) => <li key={risk}>{risk}</li>)}</ul>
            </div>
            <label className="grid gap-1.5 text-sm font-medium">Digite exatamente <span className="font-mono text-xs">{pendingPreview.confirmationText}</span><Input value={confirmationText} onChange={(event) => setConfirmationText(event.target.value)} placeholder="Texto de confirmação" autoFocus /></label>
            {confirmMutation.isError && <p className="text-sm text-destructive" role="alert">{confirmMutation.error instanceof Error ? confirmMutation.error.message : 'Não foi possível confirmar o envio.'}</p>}
          </div>}
          <DialogFooter><Button type="button" variant="outline" onClick={() => { setPendingPreview(null); setConfirmationText(''); confirmMutation.reset(); }} disabled={confirmMutation.isPending}>Cancelar</Button><Button type="button" onClick={() => confirmMutation.mutate()} disabled={!pendingPreview || confirmationText !== pendingPreview.confirmationText || confirmMutation.isPending}>{confirmMutation.isPending ? 'Enviando pelo Moodle…' : 'Confirmar e enviar'}</Button></DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
