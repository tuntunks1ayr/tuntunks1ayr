'use client';

// ============================================================
// apps/web/src/components/TradingSuite.tsx
// ============================================================
import React, {
  useCallback, useEffect, useMemo, useRef, useState,
} from 'react';
import { create } from 'zustand';
import { createChart, IChartApi, ISeriesApi } from 'lightweight-charts';

// ============================================================
// Типы (в реальном проекте — packages/shared/types.ts)
// ============================================================
type Side = 'BUY' | 'SELL';

interface BookLevel { price: number; quantity: number; orderCount: number; }

interface OrderBookState {
  bids: BookLevel[];
  asks: BookLevel[];
  bestBid: number | null;
  bestAsk: number | null;
  sequence: number;
}

interface TradeMsg {
  id: string; price: number; quantity: number; side: Side; ts: number;
}

interface ChatMsg {
  id: string; senderId: string; senderName: string;
  content: string; ts: number;
}

interface Pair { symbol: string; base: string; quote: string; lastPrice: number; change24h: number; }
interface ChatSummary { id: string; title: string; lastMessage: string; unread: number; ts: number; }

// ============================================================
// Zustand store
// ============================================================
interface Store {
  balance: Record<string, number>;
  pairs: Pair[];
  chats: ChatSummary[];
  selectedPair: string;
  selectedChat: string;
  orderBook: Record<string, OrderBookState>;
  trades: TradeMsg[];
  messages: ChatMsg[];
  myOrders: any[];
  setSelectedPair: (s: string) => void;
  setSelectedChat: (s: string) => void;
  upsertBook: (symbol: string, book: OrderBookState) => void;
  pushTrade: (t: TradeMsg) => void;
  pushMessage: (m: ChatMsg) => void;
}

const useStore = create<Store>((set) => ({
  balance: { USDT: 25_000, BTC: 0.42, ETH: 3.1 },
  pairs: [],
  chats: [],
  selectedPair: 'BTC/USDT',
  selectedChat: 'chat-1',
  orderBook: {},
  trades: [],
  messages: [],
  myOrders: [],
  setSelectedPair: (s) => set({ selectedPair: s }),
  setSelectedChat: (s) => set({ selectedChat: s }),
  upsertBook: (symbol, book) =>
    set((st) => ({ orderBook: { ...st.orderBook, [symbol]: book } })),
  pushTrade: (t) => set((st) => ({ trades: [t, ...st.trades].slice(0, 50) })),
  pushMessage: (m) => set((st) => ({ messages: [...st.messages, m] })),
}));

// ============================================================
// WebSocket hook (одно соединение на всё приложение)
// ============================================================
function useRealtimeSocket() {
  const wsRef = useRef<WebSocket | null>(null);
  const [connected, setConnected] = useState(false);
  const pendingRef = useRef<string[]>([]);

  useEffect(() => {
    const token = localStorage.getItem('jwt') || '';
    const ws = new WebSocket(`${process.env.NEXT_PUBLIC_WS_URL}?token=${token}`);
    wsRef.current = ws;

    ws.onopen = () => {
      setConnected(true);
      // Флашим отложенные подписки
      for (const msg of pendingRef.current) ws.send(msg);
      pendingRef.current = [];
    };
    ws.onclose = () => setConnected(false);
    ws.onmessage = (ev) => {
      try {
        const msg = JSON.parse(ev.data);
        handleServerMessage(msg);
      } catch (e) { /* ignore */ }
    };
    return () => ws.close();
  }, []);

  const send = useCallback((obj: any) => {
    const data = JSON.stringify(obj);
    if (wsRef.current?.readyState === WebSocket.OPEN) {
      wsRef.current.send(data);
    } else {
      pendingRef.current.push(data);
    }
  }, []);

  const subscribe = useCallback((topics: string[]) => send({ op: 'subscribe', topics }), [send]);
  const unsubscribe = useCallback((topics: string[]) => send({ op: 'unsubscribe', topics }), [send]);

  return { connected, send, subscribe, unsubscribe };
}

/** Обработчик всех входящих сообщений — роутинг по op/топику. */
function handleServerMessage(msg: any) {
  const store = useStore.getState();
  switch (msg.op) {
    case 'snapshot':
      if (msg.topic?.startsWith('orderbook:')) {
        store.upsertBook(msg.topic.split(':')[1], msg.data);
      }
      break;
    case 'trade':
      store.pushTrade(msg.data);
      break;
    case 'message':
      store.pushMessage(msg);
      break;
    case 'book-update': {
      const symbol = msg.topic.split(':')[1];
      const prev = store.orderBook[symbol];
      if (!prev) return;
      // Применяем дельты (упрощённо — заменяем затронутые уровни)
      store.upsertBook(symbol, {
        ...prev,
        bids: applyLevels(prev.bids, msg.data.bids),
        asks: applyLevels(prev.asks, msg.data.asks),
        bestBid: msg.data.bids[0]?.price ?? prev.bestBid,
        bestAsk: msg.data.asks[0]?.price ?? prev.bestAsk,
        sequence: msg.data.sequence,
      });
      break;
    }
  }
}

function applyLevels(old: BookLevel[], changes: BookLevel[]): BookLevel[] {
  const map = new Map(old.map(l => [l.price, l]));
  for (const c of changes) {
    if (c.quantity === 0) map.delete(c.price);
    else map.set(c.price, c);
  }
  const arr = Array.from(map.values());
  return arr;
}

// ============================================================
// Заглушка REST (в реальном проекте — TanStack Query + fetch)
// ============================================================
async function fetchJSON<T>(url: string): Promise<T> {
  const r = await fetch(url, { headers: { Authorization: `Bearer ${localStorage.getItem('jwt')}` } });
  if (!r.ok) throw new Error(`${url}: ${r.status}`);
  return r.json();
}

// ============================================================
// Главный компонент
// ============================================================
export default function TradingSuite() {
  const { connected, send, subscribe } = useRealtimeSocket();
  const {
    balance, pairs, chats, selectedPair, selectedChat,
    orderBook, trades, messages, myOrders,
    setSelectedPair, setSelectedChat,
  } = useStore();

  const [leftTab, setLeftTab] = useState<'chats' | 'markets'>('markets');
  const [centerTab, setCenterTab] = useState<'chart' | 'chat'>('chart');
  const [formSide, setFormSide] = useState<Side>('BUY');
  const [formPrice, setFormPrice] = useState('');
  const [formQty, setFormQty] = useState('');

  // ---------- Инициализация ----------
  useEffect(() => {
    (async () => {
      try {
        const [p, c] = await Promise.all([
          fetchJSON<Pair[]>('/api/markets/pairs'),
          fetchJSON<ChatSummary[]>('/api/chats'),
        ]);
        useStore.setState({ pairs: p, chats: c });
        if (p.length && !p.find(x => x.symbol === selectedPair)) {
          setSelectedPair(p[0].symbol);
        }
      } catch (e) {
        console.error('init failed', e);
        // Demo fallback — чтобы прототип работал без бэкенда
        useStore.setState({
          pairs: [
            { symbol: 'BTC/USDT', base: 'BTC', quote: 'USDT', lastPrice: 67432, change24h: 2.4 },
            { symbol: 'ETH/USDT', base: 'ETH', quote: 'USDT', lastPrice: 3456,  change24h: -1.2 },
            { symbol: 'TON/USDT', base: 'TON', quote: 'USDT', lastPrice: 6.42,  change24h: 5.7 },
          ],
          chats: [
            { id: 'chat-1', title: 'BTC/USDT P2P',      lastMessage: 'Куплю 0.5 BTC',    unread: 2, ts: Date.now() - 60_000 },
            { id: 'chat-2', title: 'Alice (Verkäufer)', lastMessage: 'Скинул реквизиты', unread: 0, ts: Date.now() - 300_000 },
            { id: 'chat-3', title: 'Support',            lastMessage: 'Чем помочь?',      unread: 0, ts: Date.now() - 3_600_000 },
          ],
        });
      }
    })();
  }, []);

  // ---------- Подписка на выбранную пару ----------
  useEffect(() => {
    if (!connected) return;
    const topics = [
      `orderbook:${selectedPair}`,
      `trades:${selectedPair}`,
      `candles:${selectedPair}:1m`,
      `user:${localStorage.getItem('userId')}`,
    ];
    subscribe(topics);
    return () => {
      // при смене пары отписываемся от старой
      send({ op: 'unsubscribe', topics: topics.filter(t => !t.startsWith('user')) });
    };
  }, [connected, selectedPair, subscribe, send]);

  // ---------- Подписка на выбранный чат ----------
  useEffect(() => {
    if (!connected || !selectedChat) return;
    subscribe([`chat:${selectedChat}`]);
    return () => { send({ op: 'unsubscribe', topics: [`chat:${selectedChat}`] }); };
  }, [connected, selectedChat, subscribe, send]);

  // ---------- Отправка ордера ----------
  const submitOrder = useCallback(async () => {
    const price = parseFloat(formPrice);
    const qty = parseFloat(formQty);
    if (!isFinite(price) || !isFinite(qty) || price <= 0 || qty <= 0) return;
    try {
      await fetch('/api/orders', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${localStorage.getItem('jwt')}` },
        body: JSON.stringify({ symbol: selectedPair, side: formSide, type: 'LIMIT', price, quantity: qty }),
      });
      setFormPrice(''); setFormQty('');
    } catch (e) { console.error('submit failed', e); }
  }, [formPrice, formQty, formSide, selectedPair]);

  // ---------- Данные ----------
  const book = orderBook[selectedPair] ?? { bids: [], asks: [], bestBid: null, bestAsk: null, sequence: 0 };
  const pair = pairs.find(p => p.symbol === selectedPair);
  const currentChat = chats.find(c => c.id === selectedChat);

  return (
    <div className="h-screen w-screen bg-[#0e1621] text-[#e6ecf2] flex overflow-hidden font-sans">

      {/* ================================================ */}
      {/* ЛЕВАЯ ПАНЕЛЬ: чаты + пары                         */}
      {/* ================================================ */}
      <aside className="w-72 flex-shrink-0 border-r border-[#1c2733] flex flex-col">
        <div className="p-3 border-b border-[#1c2733]">
          <div className="flex bg-[#17212b] rounded-lg p-1">
            <button
              onClick={() => setLeftTab('markets')}
              className={`flex-1 py-1.5 rounded-md text-sm font-medium ${leftTab === 'markets' ? 'bg-[#2481cc] text-white' : 'text-[#8b98a5]'}`}
            >Markets</button>
            <button
              onClick={() => setLeftTab('chats')}
              className={`flex-1 py-1.5 rounded-md text-sm font-medium ${leftTab === 'chats' ? 'bg-[#2481cc] text-white' : 'text-[#8b98a5]'}`}
            >Chats</button>
          </div>
        </div>

        <div className="flex-1 overflow-y-auto">
          {leftTab === 'markets' && (
            <div>
              {pairs.map(p => (
                <button
                  key={p.symbol}
                  onClick={() => setSelectedPair(p.symbol)}
                  className={`w-full px-4 py-3 flex items-center justify-between border-b border-[#1c2733]/50 hover:bg-[#17212b] ${selectedPair === p.symbol ? 'bg-[#17212b]' : ''}`}
                >
                  <div className="text-left">
                    <div className="text-sm font-semibold">{p.symbol}</div>
                    <div className="text-xs text-[#8b98a5]">{p.base} / {p.quote}</div>
                  </div>
                  <div className="text-right">
                    <div className="text-sm font-mono">${p.lastPrice.toLocaleString()}</div>
                    <div className={`text-xs font-mono ${p.change24h >= 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
                      {p.change24h >= 0 ? '+' : ''}{p.change24h}%
                    </div>
                  </div>
                </button>
              ))}
            </div>
          )}

          {leftTab === 'chats' && (
            <div>
              {chats.map(c => (
                <button
                  key={c.id}
                  onClick={() => { setSelectedChat(c.id); setCenterTab('chat'); }}
                  className={`w-full px-4 py-3 flex items-start gap-3 border-b border-[#1c2733]/50 hover:bg-[#17212b] ${selectedChat === c.id ? 'bg-[#17212b]' : ''}`}
                >
                  <div className="w-10 h-10 rounded-full bg-[#2481cc] flex items-center justify-center font-bold">
                    {c.title.charAt(0)}
                  </div>
                  <div className="flex-1 text-left min-w-0">
                    <div className="flex items-center justify-between">
                      <span className="text-sm font-semibold truncate">{c.title}</span>
                      {c.unread > 0 && (
                        <span className="ml-2 bg-[#2481cc] text-white text-xs rounded-full px-2 py-0.5">{c.unread}</span>
                      )}
                    </div>
                    <div className="text-xs text-[#8b98a5] truncate">{c.lastMessage}</div>
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>

        <div className="p-3 border-t border-[#1c2733] text-xs">
          <div className={`flex items-center gap-2 ${connected ? 'text-emerald-400' : 'text-rose-400'}`}>
            <span className={`w-2 h-2 rounded-full ${connected ? 'bg-emerald-400' : 'bg-rose-400'} animate-pulse`} />
            {connected ? 'Live' : 'Reconnecting...'}
          </div>
        </div>
      </aside>

      {/* ================================================ */}
      {/* ЦЕНТР: график + чат                               */}
      {/* ================================================ */}
      <main className="flex-1 flex flex-col min-w-0">
        <div className="border-b border-[#1c2733] flex items-center px-4 h-14 gap-4">
          <div>
            <div className="text-lg font-bold">{selectedPair}</div>
            <div className="text-xs text-[#8b98a5]">
              {pair ? `$${pair.lastPrice.toLocaleString()} · ${pair.change24h >= 0 ? '+' : ''}${pair.change24h}%` : '—'}
            </div>
          </div>
          <div className="ml-auto flex gap-2 text-sm">
            <button
              onClick={() => setCenterTab('chart')}
              className={`px-4 py-1.5 rounded-md ${centerTab === 'chart' ? 'bg-[#2481cc] text-white' : 'text-[#8b98a5]'}`}
            >Chart</button>
            <button
              onClick={() => setCenterTab('chat')}
              className={`px-4 py-1.5 rounded-md ${centerTab === 'chat' ? 'bg-[#2481cc] text-white' : 'text-[#8b98a5]'}`}
            >Chat{currentChat ? ` · ${currentChat.title}` : ''}</button>
          </div>
        </div>

        <div className="flex-1 min-h-0">
          {centerTab === 'chart' && <ChartPanel symbol={selectedPair} />}
          {centerTab === 'chat' && <ChatPanel chatId={selectedChat} messages={messages} onSend={(content) => send({ op: 'message', payload: { chatId: selectedChat, content } })} onTyping={() => send({ op: 'typing', payload: { chatId: selectedChat } })} />}
        </div>

        {/* Нижняя панель с открытыми ордерами */}
        <div className="h-40 border-t border-[#1c2733] overflow-y-auto">
          <div className="px-4 py-2 text-xs font-semibold text-[#8b98a5] uppercase">Open Orders</div>
          {myOrders.length === 0 ? (
            <div className="text-center text-xs text-[#8b98a5] py-4">Нет открытых ордеров</div>
          ) : (
            <table className="w-full text-xs font-mono">
              <thead className="text-[#8b98a5] text-left">
                <tr><th className="px-4 py-1">Pair</th><th>Side</th><th>Price</th><th>Qty</th><th>Filled</th></tr>
              </thead>
              <tbody>
                {myOrders.map((o, i) => (
                  <tr key={i} className="border-t border-[#1c2733]/50">
                    <td className="px-4 py-1">{o.symbol}</td>
                    <td className={o.side === 'BUY' ? 'text-emerald-400' : 'text-rose-400'}>{o.side}</td>
                    <td>{o.price}</td>
                    <td>{o.quantity}</td>
                    <td>{o.filledQty}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </main>

      {/* ================================================ */}
      {/* ПРАВО: стакан + форма                             */}
      {/* ================================================ */}
      <aside className="w-80 flex-shrink-0 border-l border-[#1c2733] flex flex-col">
        {/* Стакан */}
        <div className="p-3 border-b border-[#1c2733]">
          <div className="flex justify-between text-xs text-[#8b98a5] mb-1 font-semibold uppercase">
            <span>Price ({pair?.quote})</span>
            <span>Size ({pair?.base})</span>
          </div>

          {/* Asks */}
          <div className="font-mono text-xs space-y-0.5 mb-1">
            {[...book.asks].reverse().map((a, i) => (
              <BookRow key={i} side="SELL" price={a.price} qty={a.quantity} maxQty={maxQty(book.asks)} />
            ))}
          </div>

          {/* Best price */}
          <div className="py-2 text-center text-xl font-bold font-mono border-y border-[#1c2733]">
            <span className="text-emerald-400">${(book.bestBid ?? 0).toLocaleString()}</span>
            <span className="text-[#8b98a5] mx-2">/</span>
            <span className="text-rose-400">${(book.bestAsk ?? 0).toLocaleString()}</span>
          </div>

          {/* Bids */}
          <div className="font-mono text-xs space-y-0.5 mt-1">
            {book.bids.map((b, i) => (
              <BookRow key={i} side="BUY" price={b.price} qty={b.quantity} maxQty={maxQty(book.bids)} />
            ))}
          </div>
        </div>

        {/* Форма ордера */}
        <div className="p-3 border-b border-[#1c2733]">
          <div className="flex bg-[#17212b] rounded-lg p-1 mb-3">
            <button
              onClick={() => setFormSide('BUY')}
              className={`flex-1 py-1.5 rounded-md text-sm font-semibold ${formSide === 'BUY' ? 'bg-emerald-500 text-white' : 'text-[#8b98a5]'}`}
            >Buy</button>
            <button
              onClick={() => setFormSide('SELL')}
              className={`flex-1 py-1.5 rounded-md text-sm font-semibold ${formSide === 'SELL' ? 'bg-rose-500 text-white' : 'text-[#8b98a5]'}`}
            >Sell</button>
          </div>

          <div className="space-y-2">
            <InputRow label="Price" value={formPrice} onChange={setFormPrice} suffix={pair?.quote} />
            <InputRow label="Amount" value={formQty} onChange={setFormQty} suffix={pair?.base} />
            <div className="flex gap-1 text-xs">
              {[25, 50, 75, 100].map(pct => (
                <button
                  key={pct}
                  onClick={() => {
                    const avail = balance[pair?.quote ?? ''] ?? 0;
                    const price = parseFloat(formPrice) || (pair?.lastPrice ?? 0);
                    setFormQty(String(((avail * pct / 100) / price).toFixed(6)));
                  }}
                  className="flex-1 py-1 bg-[#17212b] hover:bg-[#1c2733] rounded"
                >{pct}%</button>
              ))}
            </div>
            <button
              onClick={submitOrder}
              className={`w-full py-2.5 rounded-lg font-semibold ${formSide === 'BUY' ? 'bg-emerald-500 hover:bg-emerald-600' : 'bg-rose-500 hover:bg-rose-600'}`}
            >
              {formSide === 'BUY' ? 'Buy' : 'Sell'} {pair?.base}
            </button>
          </div>
        </div>

        {/* Баланс */}
        <div className="p-3 text-xs">
          <div className="text-[#8b98a5] font-semibold mb-2 uppercase">Balance</div>
          {Object.entries(balance).map(([asset, amount]) => (
            <div key={asset} className="flex justify-between py-1">
              <span className="text-[#8b98a5]">{asset}</span>
              <span className="font-mono">{Number(amount).toFixed(asset === 'USDT' ? 2 : 4)}</span>
            </div>
          ))}
        </div>

        {/* Trades */}
        <div className="flex-1 overflow-y-auto p-3">
          <div className="text-xs text-[#8b98a5] font-semibold uppercase mb-2">Recent Trades</div>
          <div className="space-y-0.5 font-mono text-xs">
            {trades.slice(0, 20).map(t => (
              <div key={t.id} className="flex justify-between">
                <span className={t.side === 'BUY' ? 'text-emerald-400' : 'text-rose-400'}>
                  {t.price.toFixed(2)}
                </span>
                <span className="text-[#8b98a5]">{t.quantity.toFixed(4)}</span>
                <span className="text-[#8b98a5]">{new Date(t.ts).toLocaleTimeString()}</span>
              </div>
            ))}
          </div>
        </div>
      </aside>
    </div>
  );
}

// ============================================================
// Подкомпоненты
// ============================================================

function maxQty(levels: BookLevel[]): number {
  return levels.reduce((m, l) => Math.max(m, l.quantity), 0) || 1;
}

function BookRow({ side, price, qty, maxQty }: { side: Side; price: number; qty: number; maxQty: number }) {
  const pct = (qty / maxQty) * 100;
  return (
    <div className="relative flex justify-between py-0.5">
      <div
        className={`absolute inset-y-0 ${side === 'BUY' ? 'right-0 bg-emerald-500/10' : 'right-0 bg-rose-500/10'}`}
        style={{ width: `${pct}%` }}
      />
      <span className={`relative ${side === 'BUY' ? 'text-emerald-400' : 'text-rose-400'}`}>
        {price.toFixed(2)}
      </span>
      <span className="relative text-[#c8d4e0]">{qty.toFixed(4)}</span>
    </div>
  );
}

function InputRow({ label, value, onChange, suffix }: {
  label: string; value: string; onChange: (v: string) => void; suffix?: string;
}) {
  return (
    <div className="flex items-center bg-[#17212b] rounded-lg px-3 py-2">
      <span className="text-xs text-[#8b98a5] w-16">{label}</span>
      <input
        type="number"
        value={value}
        onChange={e => onChange(e.target.value)}
        className="flex-1 bg-transparent outline-none text-sm font-mono text-right"
        placeholder="0.00"
      />
      {suffix && <span className="text-xs text-[#8b98a5] ml-2 w-12 text-right">{suffix}</span>}
    </div>
  );
}

// ============================================================
// ChartPanel — TradingView Lightweight Charts
// ============================================================
function ChartPanel({ symbol }: { symbol: string }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const chartRef = useRef<IChartApi | null>(null);
  const seriesRef = useRef<ISeriesApi<'candlestick'> | null>(null);

  useEffect(() => {
    if (!containerRef.current) return;

    const chart = createChart(containerRef.current, {
      layout: { background: { color: 'transparent' }, textColor: '#8b98a5' },
      grid: { vertLines: { color: '#1c2733' }, horzLines: { color: '#1c2733' } },
      crosshair: { mode: 1 },
      rightPriceScale: { borderColor: '#1c2733' },
      timeScale: { borderColor: '#1c2733', timeVisible: true },
    });
    const series = chart.addCandlestickSeries({
      upColor: '#4ade80', downColor: '#f87171',
      borderUpColor: '#4ade80', borderDownColor: '#f87171',
      wickUpColor: '#4ade80', wickDownColor: '#f87171',
    });
    chartRef.current = chart;
    seriesRef.current = series;

    // Загружаем исторические свечи
    fetch(`/api/markets/candles?symbol=${symbol}&tf=1m&limit=500`)
      .then(r => r.ok ? r.json() : [])
      .then((data: any[]) => {
        if (Array.isArray(data) && data.length) series.setData(data);
        else generateDemoCandles(series);
      })
      .catch(() => generateDemoCandles(series));

    const resize = () => chart.applyOptions({
      width: containerRef.current!.clientWidth,
      height: containerRef.current!.clientHeight,
    });
    resize();
    const ro = new ResizeObserver(resize);
    ro.observe(containerRef.current);

    return () => {
      ro.disconnect();
      chart.remove();
      chartRef.current = null;
      seriesRef.current = null;
    };
  }, [symbol]);

  // Live updates через WS
  useEffect(() => {
    const ws = new WebSocket(`${process.env.NEXT_PUBLIC_WS_URL}?token=${localStorage.getItem('jwt')}`);
    ws.onopen = () => ws.send(JSON.stringify({ op: 'subscribe', topics: [`candles:${symbol}:1m`] }));
    ws.onmessage = (ev) => {
      try {
        const msg = JSON.parse(ev.data);
        if (msg.op === 'candle' && seriesRef.current) {
          seriesRef.current.update(msg.data);
        }
      } catch (e) {}
    };
    return () => ws.close();
  }, [symbol]);

  return <div ref={containerRef} className="w-full h-full" />;
}

function generateDemoCandles(series: ISeriesApi<'candlestick'>) {
  const now = Math.floor(Date.now() / 1000);
  const data = [];
  let price = 67000;
  for (let i = 500; i >= 0; i--) {
    const t = now - i * 60;
    const o = price;
    price += (Math.random() - 0.5) * 100;
    const c = price;
    const h = Math.max(o, c) + Math.random() * 50;
    const l = Math.min(o, c) - Math.random() * 50;
    data.push({ time: t as any, open: o, high: h, low: l, close: c });
  }
  series.setData(data);
}

// ============================================================
// ChatPanel
// ============================================================
function ChatPanel({ chatId, messages, onSend, onTyping }: {
  chatId: string;
  messages: ChatMsg[];
  onSend: (content: string) => void;
  onTyping: () => void;
}) {
  const [input, setInput] = useState('');
  const bottomRef = useRef<HTMLDivElement>(null);
  const myId = localStorage.getItem('userId');

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages.length]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const txt = input.trim();
    if (!txt) return;
    onSend(txt);
    setInput('');
  };

  return (
    <div className="h-full flex flex-col">
      <div className="flex-1 overflow-y-auto p-4 space-y-3">
        {messages.filter(m => m.senderId === chatId || true).map(m => {
          const mine = m.senderId === myId;
          return (
            <div key={m.id} className={`flex ${mine ? 'justify-end' : 'justify-start'}`}>
              <div className={`max-w-[70%] px-3 py-2 rounded-2xl ${mine ? 'bg-[#2481cc] text-white' : 'bg-[#17212b] text-[#e6ecf2]'}`}>
                {!mine && <div className="text-xs font-semibold text-[#6ab2f2] mb-0.5">{m.senderName || 'User'}</div>}
                <div className="text-sm break-words">{m.content}</div>
                <div className="text-[10px] opacity-60 text-right mt-0.5">
                  {new Date(m.ts).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </div>
              </div>
            </div>
          );
        })}
        <div ref={bottomRef} />
      </div>

      <form onSubmit={handleSubmit} className="p-3 border-t border-[#1c2733] flex gap-2">
        <input
          value={input}
          onChange={e => { setInput(e.target.value); onTyping(); }}
          placeholder="Написать сообщение..."
          className="flex-1 bg-[#17212b] px-4 py-2 rounded-lg outline-none text-sm"
        />
        <button
          type="submit"
          className="bg-[#2481cc] hover:bg-[#2a90e0] px-4 py-2 rounded-lg text-sm font-semibold"
        >Send</button>
      </form>
    </div>
  );
}
