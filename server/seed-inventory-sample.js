const { sql, connection } = require('./db')

const ingredients = [
  ['CFBEAN', 'Hạt cà phê rang', 'Cà phê', 'g', 500],
  ['WATER', 'Nước lọc', 'Nền pha chế', 'ml', 5000],
  ['ICE', 'Đá viên', 'Nền pha chế', 'g', 5000],
  ['FRESHMILK', 'Sữa tươi', 'Sữa & kem', 'ml', 2000],
  ['CONDMILK', 'Sữa đặc', 'Sữa & kem', 'ml', 1000],
  ['SUGARSYRUP', 'Syrup đường', 'Syrup', 'ml', 1000],
  ['MATCHA', 'Bột matcha', 'Trà & matcha', 'g', 200],
  ['BLACKTEA', 'Trà đen', 'Trà & matcha', 'g', 300],
  ['PEACHSYRUP', 'Syrup đào', 'Syrup', 'ml', 500],
  ['ORANGEJUICE', 'Nước cam', 'Trái cây', 'ml', 1000],
  ['LEMONGRASS', 'Sả tươi', 'Trái cây', 'g', 300],
  ['HAZELNUT', 'Syrup hạt dẻ', 'Syrup', 'ml', 500],
  ['COLDBREW', 'Cốt cold brew', 'Cà phê', 'ml', 1000],
  ['WHIPCREAM', 'Whipping cream', 'Sữa & kem', 'ml', 500],
  ['CREAMCHEESE', 'Cream cheese', 'Sữa & kem', 'g', 500],
  ['SALT', 'Muối', 'Gia vị', 'g', 100],
  ['WHITEPEARL', 'Trân châu trắng', 'Topping', 'g', 500],
  ['COFFEEJELLY', 'Thạch cà phê', 'Topping', 'g', 500]
]

const normalize = value => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').toLowerCase()
const productRecipe = name => {
  const n = normalize(name)
  if (n.includes('ca phe muoi')) return [['CFBEAN',18],['CONDMILK',30],['WHIPCREAM',30],['SALT',1],['ICE',100]]
  if (n.includes('ca phe sua')) return [['CFBEAN',18],['CONDMILK',30],['WATER',60],['ICE',100]]
  if (n.includes('americano')) return [['CFBEAN',18],['WATER',180],['ICE',120]]
  if (n.includes('latte hat de')) return [['CFBEAN',18],['FRESHMILK',180],['HAZELNUT',15],['ICE',100]]
  if (n.includes('cold brew') && n.includes('cam')) return [['COLDBREW',150],['ORANGEJUICE',50],['SUGARSYRUP',10],['ICE',120]]
  if (n.includes('matcha')) return [['MATCHA',4],['FRESHMILK',180],['SUGARSYRUP',10],['ICE',100]]
  if (n.includes('tra dao cam sa')) return [['BLACKTEA',8],['PEACHSYRUP',25],['ORANGEJUICE',40],['LEMONGRASS',10],['SUGARSYRUP',10],['ICE',120]]
  if (n.includes('tra dao')) return [['BLACKTEA',8],['PEACHSYRUP',25],['SUGARSYRUP',10],['ICE',120]]
  if (n.includes('tra trai cay')) return [['BLACKTEA',8],['PEACHSYRUP',20],['ORANGEJUICE',30],['SUGARSYRUP',10],['ICE',120]]
  if (n.includes('ca phe') || n.includes('coffee')) return [['CFBEAN',18],['WATER',120],['ICE',100]]
  if (n.includes('tra')) return [['BLACKTEA',8],['SUGARSYRUP',10],['ICE',120]]
  return []
}

const toppingRecipe = name => {
  const n = normalize(name)
  if (n.includes('tran chau trang')) return [['WHITEPEARL',50]]
  if (n.includes('thach ca phe')) return [['COFFEEJELLY',50]]
  if (n.includes('kem cheese')) return [['CREAMCHEESE',25],['WHIPCREAM',15]]
  if (n.includes('shot espresso')) return [['CFBEAN',18]]
  return []
}

async function run() {
  const pool = await connection
  const transaction = new sql.Transaction(pool)
  await transaction.begin()
  try {
    const store = (await new sql.Request(transaction).query('SELECT TOP 1 Id FROM dbo.Stores ORDER BY CreatedAt')).recordset[0]
    if (!store) throw new Error('Chưa có cửa hàng trong cơ sở dữ liệu.')

    for (const [code,name,category,unit,reorder] of ingredients) {
      await new sql.Request(transaction).input('store',sql.UniqueIdentifier,store.Id).input('code',sql.NVarChar,code).input('name',sql.NVarChar,name).input('category',sql.NVarChar,category).input('unit',sql.NVarChar,unit).input('reorder',sql.Decimal(18,3),reorder).query("IF NOT EXISTS(SELECT 1 FROM dbo.Ingredients WHERE StoreId=@store AND Code=@code) INSERT dbo.Ingredients(StoreId,Code,Name,Category,Unit,Quantity,ReorderLevel,AverageCost,IsActive) VALUES(@store,@code,@name,@category,@unit,0,@reorder,0,1)")
    }
    const ingredientRows = (await new sql.Request(transaction).input('store',sql.UniqueIdentifier,store.Id).query('SELECT Id,Code FROM dbo.Ingredients WHERE StoreId=@store')).recordset
    const ingredientByCode = new Map(ingredientRows.map(row => [row.Code,row.Id]))
    const products = (await new sql.Request(transaction).input('store',sql.UniqueIdentifier,store.Id).query("SELECT p.Id,p.Name,c.Name Category FROM dbo.Products p JOIN dbo.Categories c ON c.Id=p.CategoryId WHERE c.StoreId=@store AND p.IsActive=1")).recordset

    let addedToppings = 0
    let addedRecipeLines = 0
    for (const product of products) {
      const normalizedCategory = normalize(product.Category)
      const normalizedName = normalize(product.Name)
      if (normalizedCategory.includes('banh') || normalizedName.includes('banh') || normalizedName.includes('croissant')) continue
      const names = !normalizedName.includes('matcha') && (normalizedName.includes('ca phe') || normalizedName.includes('coffee') || normalizedName.includes('americano') || normalizedName.includes('latte') || normalizedName.includes('cold brew'))
        ? [['Thạch cà phê',8000],['Extra shot espresso',15000],['Kem cheese',12000]]
        : [['Trân châu trắng',10000],['Kem cheese',12000]]
      for (const [name,price] of names) {
        const request = new sql.Request(transaction).input('product',sql.UniqueIdentifier,product.Id).input('name',sql.NVarChar,name).input('price',sql.Int,price)
        const result = await request.query("IF NOT EXISTS(SELECT 1 FROM dbo.ProductToppings WHERE ProductId=@product AND Name=@name) BEGIN INSERT dbo.ProductToppings(ProductId,Name,Price,IsActive) VALUES(@product,@name,@price,1); SELECT CAST(1 AS bit) Added END ELSE SELECT CAST(0 AS bit) Added")
        if (result.recordset[0].Added) addedToppings++
      }
      for (const [code,quantity] of productRecipe(product.Name)) {
        const ingredientId = ingredientByCode.get(code)
        if (!ingredientId) continue
        const result = await new sql.Request(transaction).input('product',sql.UniqueIdentifier,product.Id).input('ingredient',sql.UniqueIdentifier,ingredientId).input('quantity',sql.Decimal(18,3),quantity).query("IF NOT EXISTS(SELECT 1 FROM dbo.RecipeComponents WHERE ProductId=@product AND IngredientId=@ingredient) BEGIN INSERT dbo.RecipeComponents(ProductId,IngredientId,Quantity) VALUES(@product,@ingredient,@quantity); SELECT CAST(1 AS bit) Added END ELSE SELECT CAST(0 AS bit) Added")
        if (result.recordset[0].Added) addedRecipeLines++
      }
    }

    const toppings = (await new sql.Request(transaction).input('store',sql.UniqueIdentifier,store.Id).query('SELECT t.Id,t.Name FROM dbo.ProductToppings t JOIN dbo.Products p ON p.Id=t.ProductId JOIN dbo.Categories c ON c.Id=p.CategoryId WHERE c.StoreId=@store AND t.IsActive=1')).recordset
    for (const topping of toppings) for (const [code,quantity] of toppingRecipe(topping.Name)) {
      const ingredientId = ingredientByCode.get(code)
      if (!ingredientId) continue
      const result = await new sql.Request(transaction).input('topping',sql.UniqueIdentifier,topping.Id).input('ingredient',sql.UniqueIdentifier,ingredientId).input('quantity',sql.Decimal(18,3),quantity).query("IF NOT EXISTS(SELECT 1 FROM dbo.RecipeComponents WHERE ToppingId=@topping AND IngredientId=@ingredient) BEGIN INSERT dbo.RecipeComponents(ToppingId,IngredientId,Quantity) VALUES(@topping,@ingredient,@quantity); SELECT CAST(1 AS bit) Added END ELSE SELECT CAST(0 AS bit) Added")
      if (result.recordset[0].Added) addedRecipeLines++
    }
    await transaction.commit()
    console.log(`Đã chuẩn bị ${ingredients.length} nguyên liệu mẫu, thêm ${addedToppings} topping và ${addedRecipeLines} dòng định lượng.`)
    console.log('Tồn đầu của nguyên liệu mới là 0. Hãy dùng Phiếu nhập kho để nhập số lượng thực tế.')
    await pool.close()
  } catch (error) {
    await transaction.rollback()
    await pool.close()
    throw error
  }
}

run().catch(error => { console.error(error.message); process.exit(1) })
