import { useState } from 'react';
import { API_URL } from '../config';

interface DemoDataGeneratorProps {
  onEventSent?: () => void;
  onChildSelected?: (childId: string) => void;
}

type BehaviorStatus = 'Nice' | 'Naughty';

const DEMO_ITEMS = [
  { name: '🎮 PlayStation 5', category: 'Electronics', budget: 500 },
  { name: '🚴 Mountain Bike', category: 'Sports', budget: 350 },
  { name: '🎨 Art Supply Kit', category: 'Creative', budget: 75 },
  { name: '📚 Book Collection', category: 'Books', budget: 100 },
  { name: '🎸 Electric Guitar', category: 'Music', budget: 400 },
  { name: '🧸 Giant Teddy Bear', category: 'Toys', budget: 50 },
  { name: '⚽ Soccer Ball', category: 'Sports', budget: 30 },
  { name: '🎮 Nintendo Switch', category: 'Electronics', budget: 300 },
  { name: '🎭 Theater Tickets', category: 'Entertainment', budget: 120 },
  { name: '🎿 Ski Equipment', category: 'Sports', budget: 600 },
];

const DEMO_CHILDREN = [
  'child-emma-2015',
  'child-noah-2014',
  'child-sophia-2013',
  'child-liam-2016',
  'child-olivia-2015',
  'child-demo-01',
  'child-demo-02',
  'child-demo-03',
];

export const DemoDataGenerator: React.FC<DemoDataGeneratorProps> = ({ onEventSent, onChildSelected }) => {
  const [loading, setLoading] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [selectedChild, setSelectedChild] = useState(DEMO_CHILDREN[0]);
  const [selectedItem, setSelectedItem] = useState(DEMO_ITEMS[0]);
  const [customChild, setCustomChild] = useState('');
  const [customItem, setCustomItem] = useState('');
  const [customCategory, setCustomCategory] = useState('');
  const [customBudget, setCustomBudget] = useState('');
  const [useCustom, setUseCustom] = useState(false);
  const [lastSubmittedChildId, setLastSubmittedChildId] = useState<string | null>(null);

  const sendWishlistEvent = async () => {
    setLoading(true);
    setMessage(null);

    const childId = useCustom && customChild ? customChild : selectedChild;
    const itemText = useCustom && customItem ? customItem : selectedItem.name;
    const category = useCustom && customCategory ? customCategory : selectedItem.category;
    const budget = useCustom && customBudget ? parseInt(customBudget) : selectedItem.budget;

    console.log('[DemoDataGen] Sending event to:', `${API_URL}/api/v1/children/${childId}/wishlist-items`);
    console.log('[DemoDataGen] Payload:', { text: itemText, category, budgetEstimate: budget });

    try {
      const response = await fetch(`${API_URL}/api/v1/children/${childId}/wishlist-items`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'X-Role': 'operator',
        },
        body: JSON.stringify({
          text: itemText,
          category: category,
          budgetEstimate: budget,
        }),
      });

      console.log('[DemoDataGen] Response status:', response.status, response.statusText);

      if (!response.ok) {
        const errorText = await response.text();
        console.error('[DemoDataGen] Error response:', errorText);
        let errorData;
        try {
          errorData = JSON.parse(errorText);
        } catch {
          errorData = { detail: errorText || 'Unknown error' };
        }
        throw new Error(errorData.detail || errorData.message || `HTTP ${response.status}: ${response.statusText}`);
      }

      const result = await response.json();
      console.log('[DemoDataGen] Success result:', result);
      setLastSubmittedChildId(childId);
      setMessage({
        type: 'success',
        text: `✅ Event sent! Item "${itemText}" added to ${childId}'s wishlist. Drasi will process this in real-time!`,
      });
      onEventSent?.();
    } catch (error: any) {
      console.error('[DemoDataGen] Exception:', error);
      setMessage({
        type: 'error',
        text: `❌ Failed to send event: ${error.message}`,
      });
    } finally {
      setLoading(false);
    }
  };

  const sendRandomEvents = async (count: number) => {
    setLoading(true);
    setMessage(null);
    let successCount = 0;
    let failCount = 0;
    let wishlistCount = 0;
    let behaviorCount = 0;

    console.log('[DemoDataGen] Starting batch send of', count, 'events (mix of wishlist + behavior)');

    for (let i = 0; i < count; i++) {
      const randomChild = DEMO_CHILDREN[Math.floor(Math.random() * DEMO_CHILDREN.length)];
      
      // 70% wishlist items, 30% behavior updates for realistic mix
      const isBehaviorUpdate = Math.random() > 0.7;
      
      if (isBehaviorUpdate) {
        // Send behavior status change
        const randomStatus: BehaviorStatus = Math.random() > 0.5 ? 'Nice' : 'Naughty';
        const statusMessage = randomStatus === 'Nice' 
          ? 'I will continue to be helpful and kind!' 
          : 'I will try to behave better and do my chores.';
        
        console.log(`[DemoDataGen] Sending behavior event ${i+1}/${count}:`, randomChild, randomStatus);
        
        try {
          const response = await fetch(`${API_URL}/api/v1/children/${randomChild}/wishlist-items`, {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              'X-Role': 'operator',
            },
            body: JSON.stringify({
              requestType: 'behavior-update',
              text: statusMessage,
              statusChange: randomStatus,
            }),
          });

          if (response.ok) {
            successCount++;
            behaviorCount++;
            console.log(`[DemoDataGen] Behavior event ${i+1}/${count} succeeded`);
          } else {
            failCount++;
            const errorText = await response.text();
            console.error(`[DemoDataGen] Behavior event ${i+1}/${count} failed:`, response.status, errorText);
          }
        } catch (error) {
          failCount++;
          console.error(`[DemoDataGen] Behavior event ${i+1}/${count} exception:`, error);
        }
      } else {
        // Send wishlist item
        const randomItem = DEMO_ITEMS[Math.floor(Math.random() * DEMO_ITEMS.length)];
        
        console.log(`[DemoDataGen] Sending wishlist event ${i+1}/${count}:`, randomChild, randomItem.name);

        try {
          const response = await fetch(`${API_URL}/api/v1/children/${randomChild}/wishlist-items`, {
            method: 'POST',
            headers: {
              'Content-Type': 'application/json',
              'X-Role': 'operator',
            },
            body: JSON.stringify({
              text: randomItem.name,
              category: randomItem.category,
              budgetEstimate: randomItem.budget,
            }),
          });

          if (response.ok) {
            successCount++;
            wishlistCount++;
            console.log(`[DemoDataGen] Wishlist event ${i+1}/${count} succeeded`);
          } else {
            failCount++;
            const errorText = await response.text();
            console.error(`[DemoDataGen] Wishlist event ${i+1}/${count} failed:`, response.status, errorText);
          }
        } catch (error) {
          failCount++;
          console.error(`[DemoDataGen] Wishlist event ${i+1}/${count} exception:`, error);
        }
      }

      // Small delay between requests
      await new Promise(resolve => setTimeout(resolve, 300));
    }

    setMessage({
      type: successCount > 0 ? 'success' : 'error',
      text: `📊 Bulk send complete: ${successCount} succeeded (${wishlistCount} wishlist, ${behaviorCount} behavior), ${failCount} failed. Drasi is processing!`,
    });
    setLoading(false);
    onEventSent?.();
  };



  return (
    <section style={{
      background: 'var(--bg-secondary)',
      padding: '2rem',
      borderRadius: 12,
      boxShadow: '0 4px 8px rgba(0,0,0,0.6)',
      border: '1px solid var(--border-medium)',
      marginBottom: '2rem'
    }}>
      <h2 style={{ marginTop: 0, color: 'var(--christmas-gold)', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
        🎁 Demo Data Generator
        <span style={{
          fontSize: '.7rem',
          background: 'var(--christmas-green)',
          color: 'white',
          padding: '4px 8px',
          borderRadius: 4,
          fontWeight: 600
        }}>
          EventHub → Drasi
        </span>
      </h2>
      <p style={{ color: 'var(--text-secondary)', marginBottom: '1.5rem' }}>
        Send events to EventHub for Drasi to process in real-time. Random events automatically include both wishlist items (70%) and behavior status changes (30%). Watch the Drasi tools detect trends, duplicates, and Naughty/Nice changes!
      </p>

      {message && (
        <div style={{
          padding: '1rem',
          background: message.type === 'success' ? 'rgba(0, 255, 0, 0.1)' : 'rgba(255, 0, 0, 0.1)',
          border: `1px solid ${message.type === 'success' ? 'var(--christmas-green)' : 'var(--santa-red)'}`,
          borderRadius: 8,
          marginBottom: '1rem',
          color: 'var(--text-primary)'
        }}>
          <div>{message.text}</div>
          {message.type === 'success' && lastSubmittedChildId && onChildSelected && (
            <button
              onClick={() => {
                onChildSelected(lastSubmittedChildId);
                setMessage(null);
              }}
              style={{
                marginTop: '0.75rem',
                padding: '0.5rem 1rem',
                background: 'var(--christmas-green)',
                color: 'white',
                border: 'none',
                borderRadius: 6,
                cursor: 'pointer',
                fontWeight: 600,
                fontSize: '0.9rem',
                boxShadow: '0 2px 4px rgba(0,0,0,0.3)'
              }}
            >
              🎁 View AI Recommendations for {lastSubmittedChildId}
            </button>
          )}
        </div>
      )}

      {loading && (
        <div style={{
          padding: '1.5rem',
          background: 'var(--bg-tertiary)',
          borderRadius: 12,
          marginBottom: '1rem',
          border: '2px dashed var(--christmas-gold)',
          textAlign: 'center'
        }}>
          <div style={{ fontSize: '2.5rem', marginBottom: '0.5rem', animation: 'pulse 1.5s ease-in-out infinite' }}>🤖</div>
          <p style={{ margin: 0, color: 'var(--text-primary)', fontWeight: 700, fontSize: '1.1rem' }}>
            Elf Agents are working...
          </p>
          <p style={{ margin: '0.5rem 0 0', color: 'var(--text-secondary)', fontSize: '.9rem' }}>
            Microsoft Agent Framework + Azure OpenAI generating personalized recommendations
          </p>
          <div style={{ 
            marginTop: '0.75rem', 
            display: 'flex', 
            justifyContent: 'center', 
            gap: '1rem',
            fontSize: '0.8rem',
            color: 'var(--text-muted)'
          }}>
            <span>📝 Analyzing wishlist</span>
            <span>🎯 Checking behavior status</span>
            <span>✨ Generating AI recommendations</span>
          </div>
        </div>
      )}

      <div style={{ display: 'flex', gap: '1rem', marginBottom: '1.5rem' }}>
        <button
          onClick={() => setUseCustom(false)}
          style={{
            padding: '0.5rem 1rem',
            background: !useCustom ? 'var(--christmas-green)' : 'var(--bg-tertiary)',
            color: !useCustom ? 'white' : 'var(--text-secondary)',
            border: 'none',
            borderRadius: 6,
            cursor: 'pointer',
            fontWeight: 600
          }}
        >
          📋 Quick Select
        </button>
        <button
          onClick={() => setUseCustom(true)}
          style={{
            padding: '0.5rem 1rem',
            background: useCustom ? 'var(--christmas-green)' : 'var(--bg-tertiary)',
            color: useCustom ? 'white' : 'var(--text-secondary)',
            border: 'none',
            borderRadius: 6,
            cursor: 'pointer',
            fontWeight: 600
          }}
        >
          ✏️ Custom
        </button>
      </div>

      {!useCustom ? (
        <div style={{ display: 'grid', gap: '1rem', marginBottom: '1.5rem' }}>
          <div>
            <label style={{ display: 'block', marginBottom: '0.5rem', color: 'var(--text-primary)', fontWeight: 600 }}>
              Child ID:
            </label>
            <select
              value={selectedChild}
              onChange={(e) => setSelectedChild(e.target.value)}
              style={{
                width: '100%',
                padding: '0.75rem',
                background: 'var(--bg-primary)',
                color: 'var(--text-primary)',
                border: '1px solid var(--border-medium)',
                borderRadius: 6,
                fontSize: '1rem'
              }}
            >
              {DEMO_CHILDREN.map(child => (
                <option key={child} value={child}>{child}</option>
              ))}
            </select>
          </div>
          <div>
            <label style={{ display: 'block', marginBottom: '0.5rem', color: 'var(--text-primary)', fontWeight: 600 }}>
              Wishlist Item:
            </label>
            <select
              value={DEMO_ITEMS.indexOf(selectedItem)}
              onChange={(e) => setSelectedItem(DEMO_ITEMS[parseInt(e.target.value)])}
              style={{
                width: '100%',
                padding: '0.75rem',
                background: 'var(--bg-primary)',
                color: 'var(--text-primary)',
                border: '1px solid var(--border-medium)',
                borderRadius: 6,
                fontSize: '1rem'
              }}
            >
              {DEMO_ITEMS.map((item, idx) => (
                <option key={idx} value={idx}>
                  {item.name} - ${item.budget} ({item.category})
                </option>
              ))}
            </select>
          </div>
        </div>
      ) : (
        <div style={{ display: 'grid', gap: '1rem', marginBottom: '1.5rem' }}>
          <div>
            <label style={{ display: 'block', marginBottom: '0.5rem', color: 'var(--text-primary)', fontWeight: 600 }}>
              Child ID:
            </label>
            <input
              type="text"
              value={customChild}
              onChange={(e) => setCustomChild(e.target.value)}
              placeholder="child-custom-123"
              style={{
                width: '100%',
                padding: '0.75rem',
                background: 'var(--bg-primary)',
                color: 'var(--text-primary)',
                border: '1px solid var(--border-medium)',
                borderRadius: 6,
                fontSize: '1rem'
              }}
            />
          </div>
          <div>
            <label style={{ display: 'block', marginBottom: '0.5rem', color: 'var(--text-primary)', fontWeight: 600 }}>
              Item Name:
            </label>
            <input
              type="text"
              value={customItem}
              onChange={(e) => setCustomItem(e.target.value)}
              placeholder="Custom Gift Item"
              style={{
                width: '100%',
                padding: '0.75rem',
                background: 'var(--bg-primary)',
                color: 'var(--text-primary)',
                border: '1px solid var(--border-medium)',
                borderRadius: 6,
                fontSize: '1rem'
              }}
            />
          </div>
          <div style={{ display: 'grid', gridTemplateColumns: '2fr 1fr', gap: '1rem' }}>
            <div>
              <label style={{ display: 'block', marginBottom: '0.5rem', color: 'var(--text-primary)', fontWeight: 600 }}>
                Category:
              </label>
              <input
                type="text"
                value={customCategory}
                onChange={(e) => setCustomCategory(e.target.value)}
                placeholder="Electronics"
                style={{
                  width: '100%',
                  padding: '0.75rem',
                  background: 'var(--bg-primary)',
                  color: 'var(--text-primary)',
                  border: '1px solid var(--border-medium)',
                  borderRadius: 6,
                  fontSize: '1rem'
                }}
              />
            </div>
            <div>
              <label style={{ display: 'block', marginBottom: '0.5rem', color: 'var(--text-primary)', fontWeight: 600 }}>
                Budget ($):
              </label>
              <input
                type="number"
                value={customBudget}
                onChange={(e) => setCustomBudget(e.target.value)}
                placeholder="100"
                style={{
                  width: '100%',
                  padding: '0.75rem',
                  background: 'var(--bg-primary)',
                  color: 'var(--text-primary)',
                  border: '1px solid var(--border-medium)',
                  borderRadius: 6,
                  fontSize: '1rem'
                }}
              />
            </div>
          </div>
        </div>
      )}

      <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
        <button
          onClick={sendWishlistEvent}
          disabled={loading}
          style={{
            padding: '1rem 2rem',
            background: loading ? 'var(--border-medium)' : 'var(--christmas-green)',
            color: 'white',
            border: 'none',
            borderRadius: 8,
            cursor: loading ? 'not-allowed' : 'pointer',
            fontSize: '1rem',
            fontWeight: 700,
            boxShadow: loading ? 'none' : '0 4px 8px rgba(0,0,0,0.5)',
            transition: 'all 0.3s'
          }}
        >
          {loading ? '⏳ Sending...' : '🚀 Send Single Event'}
        </button>
        <button
          onClick={() => sendRandomEvents(5)}
          disabled={loading}
          style={{
            padding: '1rem 2rem',
            background: loading ? 'var(--border-medium)' : 'var(--santa-red)',
            color: 'white',
            border: 'none',
            borderRadius: 8,
            cursor: loading ? 'not-allowed' : 'pointer',
            fontSize: '1rem',
            fontWeight: 700,
            boxShadow: loading ? 'none' : '0 4px 8px rgba(0,0,0,0.5)',
            transition: 'all 0.3s'
          }}
        >
          {loading ? '⏳ Sending...' : '📊 Send 5 Random Events'}
        </button>
        <button
          onClick={() => sendRandomEvents(10)}
          disabled={loading}
          style={{
            padding: '1rem 2rem',
            background: loading ? 'var(--border-medium)' : 'var(--christmas-gold)',
            color: 'var(--bg-primary)',
            border: 'none',
            borderRadius: 8,
            cursor: loading ? 'not-allowed' : 'pointer',
            fontSize: '1rem',
            fontWeight: 700,
            boxShadow: loading ? 'none' : '0 4px 8px rgba(0,0,0,0.5)',
            transition: 'all 0.3s'
          }}
        >
          {loading ? '⏳ Sending...' : '💥 Send 10 Random Events'}
        </button>
      </div>

      <div style={{
        marginTop: '1.5rem',
        padding: '1rem',
        background: 'var(--bg-tertiary)',
        borderRadius: 8,
        border: '1px solid var(--border-light)'
      }}>
        <h4 style={{ marginTop: 0, color: 'var(--christmas-green)', fontSize: '.95rem' }}>💡 How it works:</h4>
        <ol style={{ color: 'var(--text-secondary)', fontSize: '.9rem', marginBottom: 0, paddingLeft: '1.5rem' }}>
          <li>Random events include both wishlist items (70%) and behavior updates (30%)</li>
          <li>All events sent to <code>/api/v1/children/{'{'} childId{'}'}/wishlist-items</code></li>
          <li>Backend publishes to EventHub (dual-write pattern)</li>
          <li>Drasi continuous queries process events in real-time (&lt;5s)</li>
          <li>Behavior changes trigger Microsoft Agent Framework to adjust recommendations (😇 Nice = fun toys, 😈 Naughty = coal/educational items)</li>
        </ol>
      </div>
    </section>
  );
};
