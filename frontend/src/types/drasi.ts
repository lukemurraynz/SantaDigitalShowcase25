export interface DrasiInsights {
  trending: Array<{ item: string; frequency: number }>;
  duplicates: Array<{ childId: string; item: string; count: number }>;
  inactiveChildren: Array<{ childId: string; lastEventDays: number }>;
  behaviorChanges: Array<{ childId: string; oldStatus: string; newStatus: string; reason?: string }>;
  stats: {
    totalEvents: number;
    activeQueries: number;
    lastUpdateSeconds: number;
  };
}

export interface YearOverYearTrends {
  current: Array<{ item: string; frequency: number; period: string }>;
  historical: Array<{ item: string; frequency: number; period: string }>;
  insights: {
    returningFavorites: string[];
    newTrends: string[];
    noLongerTrending: string[];
    volumeChange: {
      current: number;
      historical: number;
      percentChange: number;
      trend: "up" | "down" | "stable";
    };
  };
  metadata: {
    currentPeriod: string;
    historicalPeriod: string;
    currentYear: number;
    comparisonYear: number;
  };
}
