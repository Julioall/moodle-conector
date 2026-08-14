import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { BookOpen, MessageCircle, RefreshCw } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { useConnectionScope } from '@/features/connections/useConnectionScope';
import { coursesGateway } from '@/features/courses/courses-gateway';
import { forumsGateway } from './forums-gateway';

function formatDate(value?: string) {
  if (!value) return '—';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleString('pt-BR');
}

export function ForumsPage() {
  const { connectionRef, selectedConnection } = useConnectionScope();
  const [courseId, setCourseId] = useState('');
  const [forumId, setForumId] = useState('');
  const courses = useQuery({ queryKey: ['app', 'forums', 'courses', connectionRef], queryFn: () => coursesGateway.list(connectionRef, 1, 100), enabled: Boolean(connectionRef), staleTime: 60_000 });
  const forums = useQuery({ queryKey: ['app', 'forums', connectionRef, courseId], queryFn: () => forumsGateway.list(connectionRef!, courseId), enabled: Boolean(connectionRef && courseId), staleTime: 60_000 });
  const read = useQuery({ queryKey: ['app', 'forums', 'read', connectionRef, courseId, forumId], queryFn: () => forumsGateway.read(connectionRef!, courseId, forumId), enabled: Boolean(connectionRef && courseId && forumId), staleTime: 20_000 });
  const selectedForum = useMemo(() => forums.data?.data.find((forum) => forum.forumId === forumId), [forums.data?.data, forumId]);

  return <main className="content-frame" aria-labelledby="forums-title"><header className="page-heading"><div><p className="eyebrow">ACADÊMICO · MOODLE-FIRST</p><h1 id="forums-title" className="flex items-center gap-2"><MessageCircle className="h-6 w-6 text-primary" />Fóruns</h1><p>Leia discussões, posts e anexos no contexto original do Moodle.</p><p className="mt-1 text-xs text-muted-foreground">Escopo: {selectedConnection?.alias ?? connectionRef ?? 'Moodle padrão'}</p></div><Button type="button" variant="outline" onClick={() => { void forums.refetch(); void read.refetch(); }} disabled={forums.isFetching || read.isFetching}><RefreshCw className={forums.isFetching || read.isFetching ? 'animate-spin' : ''} />Atualizar</Button></header>
    <Card><CardHeader><CardTitle className="text-lg">Selecionar contexto</CardTitle><CardDescription>O conteúdo permanece somente leitura nesta primeira onda.</CardDescription></CardHeader><CardContent className="grid gap-4 md:grid-cols-2"><label className="grid gap-1.5 text-sm font-medium">Curso<select className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm" value={courseId} onChange={(event) => { setCourseId(event.target.value); setForumId(''); }}><option value="">Selecione um curso</option>{(courses.data?.data ?? []).map((course) => <option key={course.courseId} value={course.courseId}>{course.displayName ?? course.fullName}</option>)}</select></label><label className="grid gap-1.5 text-sm font-medium">Fórum<Select value={forumId || 'none'} onValueChange={(value) => setForumId(value === 'none' ? '' : value)} disabled={!courseId || forums.isPending}><SelectTrigger><SelectValue placeholder="Selecione um fórum" /></SelectTrigger><SelectContent><SelectItem value="none">Selecione um fórum</SelectItem>{(forums.data?.data ?? []).map((forum) => <SelectItem key={forum.forumId} value={forum.forumId}>{forum.name ?? forum.forumId}</SelectItem>)}</SelectContent></Select></label></CardContent></Card>
    {!connectionRef && <Card><CardContent className="p-8 text-sm text-muted-foreground">Configure uma conexão Moodle para consultar os fóruns.</CardContent></Card>}
    {forumId && <section className="space-y-4"><div className="flex items-end justify-between gap-3"><div><h2 className="text-lg font-semibold">{read.data?.data.forumName ?? selectedForum?.name ?? 'Discussões'}</h2><p className="text-sm text-muted-foreground">Curso {courseId} · {read.data?.data.returnedCount ?? 0} discussões carregadas</p></div><Badge variant="outline">Somente leitura</Badge></div>{read.isPending ? <Card><CardContent className="p-8 text-sm text-muted-foreground">Carregando discussões…</CardContent></Card> : read.isError ? <Card><CardContent className="p-8 text-sm text-destructive" role="alert">Não foi possível carregar o fórum.</CardContent></Card> : <div className="space-y-3">{(read.data?.data.discussions ?? []).map((discussion) => <Card key={discussion.discussionId}><CardHeader className="pb-3"><div className="flex items-start justify-between gap-3"><div><CardTitle className="text-base">{discussion.subject}</CardTitle><CardDescription>{discussion.authorFullName ?? 'Autor não informado'} · {formatDate(discussion.createdAt)}</CardDescription></div><Badge variant="outline">{discussion.replyCount} resposta{discussion.replyCount === 1 ? '' : 's'}</Badge></div></CardHeader><CardContent><p className="whitespace-pre-wrap text-sm text-muted-foreground">{discussion.messageText || 'Sem texto de abertura.'}</p>{discussion.posts.length > 0 && <div className="mt-4 space-y-3 border-t pt-4">{discussion.posts.map((post) => <article key={post.postId} className="rounded-md bg-muted/30 p-3"><div className="flex items-center justify-between gap-2 text-xs text-muted-foreground"><span className="font-medium text-foreground">{post.userFullName ?? post.userId ?? 'Participante'}</span><span>{formatDate(post.createdAt)}</span></div><p className="mt-2 whitespace-pre-wrap text-sm">{post.messageText || post.subject}</p>{post.attachments.length > 0 && <p className="mt-2 text-xs text-muted-foreground">{post.attachments.length} anexo(s) disponível(is) no Moodle.</p>}</article>)}</div>}</CardContent></Card>)}{read.data?.data.discussions.length === 0 && <Card><CardContent className="flex flex-col items-center gap-3 p-12 text-center"><BookOpen className="h-9 w-9 text-muted-foreground/40" /><p className="text-sm text-muted-foreground">Nenhuma discussão encontrada.</p></CardContent></Card>}</div>}</section>}
  </main>;
}
