# Year-over-Year Trends Feature

## Overview
New dashboard component comparing current trending toys (from Drasi real-time queries) with historical data from last Christmas (stored in Cosmos DB).

## Components Created

### 1. Backend API (`src/services/HistoricalTrendsApi.cs`)
- **Endpoint**: `GET /api/v1/trends/year-over-year`
- **Data Sources**:
  - Current trends: Drasi continuous query `wishlist-trending-1h`
  - Historical data: Cosmos DB wishlist container (Dec 1-31, previous year)
- **Returns**:
  - Side-by-side comparison of top 10 items
  - Volume change percentage
  - Insights: returning favorites, new trends, faded items

### 2. Frontend Component (`frontend/src/components/YearOverYearPanel.tsx`)
- **Visual Features**:
  - Large percentage change indicator with trend arrow
  - Side-by-side comparison grid (current vs last year)
  - Insight cards (returning, new, faded items)
  - Auto-refresh every 60 seconds
  - Festive Christmas theme matching existing UI

### 3. Type Definitions (`frontend/src/types/drasi.ts`)
- Extended with `YearOverYearTrends` interface
- Supports comparison metadata and insights

## Integration Points

### Backend
- Registered in `Program.cs`: `v1.MapHistoricalTrendsApi()`
- Uses existing `IWishlistRepository` for Cosmos queries
- Uses existing `IDrasiViewClient` for real-time Drasi data

### Frontend
- Added to `SantaView.tsx` (Santa''s control panel)
- Positioned after notifications and parent portal
- Uses same styling system as existing components

## Demo Showcase Value

This feature demonstrates:
✅ **Drasi + Cosmos Integration**: Real-time (Drasi) + Historical (Cosmos DB)
✅ **Multi-layered Data Strategy**: Event streams, materialized views, permanent storage
✅ **Business Intelligence**: Year-over-year trend analysis
✅ **AI Agent Context**: Agents can reference both current AND historical patterns
✅ **Visual Storytelling**: Clear comparison showing what''s changed

## Future Enhancements

For production:
1. Replace simulated historical data with actual Cosmos queries
2. Add date range picker (flexible time windows)
3. Export to CSV/reports
4. Integrate as tool for Microsoft Agent Framework
5. Add more sophisticated aggregations (categories, age groups, regions)

## Technical Notes

- Historical data currently simulated (see `GetHistoricalTrendingAsync`)
- Production would query Cosmos with date filters
- API is designed to be agent-callable (could add as tool)
- Component gracefully handles empty/error states
