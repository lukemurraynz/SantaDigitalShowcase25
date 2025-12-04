import React from 'react';
import { CopilotKit } from '@copilotkit/react-core';
import { CopilotPopup } from '@copilotkit/react-ui';
import { useCopilotAction } from '@copilotkit/react-core';
import { createChild, addWishlistItem, runLogisticsAssessment } from '../agentClient';

// Separate component that registers actions inside provider context
const AssistantActions: React.FC = () => {
  useCopilotAction({
    name: 'create_child',
    description: 'Create a child profile',
    parameters: [ { name: 'childId', type: 'string', required: true } ],
    handler: async ({ childId }: any) => {
      const res = await createChild(String(childId));
      return { childId: res.childId };
    }
  });
  useCopilotAction({
    name: 'add_wishlist_item',
    description: 'Add a wishlist item for a child',
    parameters: [
      { name: 'childId', type: 'string', required: true },
      { name: 'toyName', type: 'string', required: true },
      { name: 'notes', type: 'string', required: false }
    ],
    handler: async ({ childId, toyName, notes }: any) => {
      const res = await addWishlistItem(String(childId), { toyName: String(toyName), notes: notes ? String(notes) : undefined });
      return { status: 'added', dedupeKey: res.dedupeKey };
    }
  });
  useCopilotAction({
    name: 'run_logistics_assessment',
    description: 'Run logistics assessment for child',
    parameters: [ { name: 'childId', type: 'string', required: true } ],
    handler: async ({ childId }: any) => {
      const res = await runLogisticsAssessment(String(childId));
      return { assessment: res };
    }
  });
  return null;
};

// Provider wrapper (code-split) that supplies context to actions + popup
const AssistantProvider: React.FC = () => {
  // Prefer env-provided public API key; fallback to sample key if provided
  const apiKey = (import.meta as any).env?.VITE_COPILOT_PUBLIC_API_KEY || 'ck_pub_73f5a83e57de47b331db3903382677d4';
  return (
  <CopilotKit publicApiKey={apiKey}>
    <AssistantActions />
    <CopilotPopup
      instructions={'You are an assistant for the Santa Digital Elves demo. Provide concise help and you can call actions: create_child, add_wishlist_item, run_logistics_assessment.'}
      labels={{ title: 'Elf Assistant', initial: 'Need help?' }}
    />
  </CopilotKit>
  );
};

export default AssistantProvider;