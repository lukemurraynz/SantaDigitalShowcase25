import React from 'react';

export const ParentPortalPage: React.FC = () => {
  return (
    <div style={{ fontSize:'0.85rem', lineHeight:1.4 }}>
      <h2 style={{ marginTop:0 }}>Parent Portal (Preview)</h2>
      <p style={{ opacity:0.8 }}>This early preview shows planned capabilities for guardians:</p>
      <ul style={{ paddingLeft:'1.1rem', margin:'0 0 0.75rem' }}>
        <li>Submit or edit wishlist items.</li>
        <li>View recommendation sets and logistics status.</li>
        <li>Receive notification summaries & delivery feasibility.</li>
      </ul>
      <p style={{ opacity:0.7 }}>Authentication, role-based access, and profile editing flows will be added in subsequent iterations.</p>
    </div>
  );
};

export default ParentPortalPage;