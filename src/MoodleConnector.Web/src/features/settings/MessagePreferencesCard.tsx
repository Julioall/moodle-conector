import { useEffect, useState } from 'react';
import { MessageSquare } from 'lucide-react';

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../../components/ui/card';
import { Label } from '../../components/ui/label';
import { Switch } from '../../components/ui/switch';
import {
  getStoredMessagePreferences,
  saveMessagePreferences,
  subscribeToMessagePreferences,
} from '../messages/message-preferences';

export function MessagePreferencesCard() {
  const [preferences, setPreferences] = useState(getStoredMessagePreferences);

  useEffect(() => {
    return subscribeToMessagePreferences(setPreferences);
  }, []);

  const sendOnEnter = preferences.sendOnEnter;

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-lg"><MessageSquare className="h-5 w-5" />Preferências de mensagens</CardTitle>
        <CardDescription>Controle o comportamento básico da tela de mensagens do Moodle.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex items-start justify-between gap-4 rounded-lg border border-border/60 bg-muted/20 px-4 py-3">
          <div className="space-y-1">
            <Label htmlFor="message-send-on-enter" className="cursor-pointer text-sm font-medium">Enviar mensagem com Enter</Label>
            <p className="text-xs text-muted-foreground">Quando desligado, o envio continua restrito ao botão de confirmação.</p>
          </div>
          <Switch
            id="message-send-on-enter"
            checked={sendOnEnter}
            onCheckedChange={(checked) => saveMessagePreferences({ ...preferences, sendOnEnter: checked })}
          />
        </div>
        <div className="rounded-md border bg-muted/30 p-3 text-sm">
          <p className="font-medium">{sendOnEnter ? 'Enter ativo para envio.' : 'Enter desativado para envio.'}</p>
          <p className="mt-1 text-xs text-muted-foreground">A preferência fica salva apenas neste dispositivo.</p>
        </div>
      </CardContent>
    </Card>
  );
}
